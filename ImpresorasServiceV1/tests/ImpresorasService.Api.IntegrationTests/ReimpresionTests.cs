using System.Security.Claims;
using System.Text.Json;
using ImpresorasService.Api.Controllers;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// Reimprimir lo que ya salió (decisión del usuario, 26/10/2026): se puede, sin límite de veces, pero
/// siempre con motivo y con quién, en un evento REPRINT que es el registro que consulta la tienda.
/// Por el reintento no se cuela ninguna copia sin registrar.
/// </summary>
public sealed class ReimpresionTests
{
    private const string Jefe = "ERP 123 Ana López";

    [Theory]
    [InlineData(PrintJobStatus.PrintedConfirmed)]
    [InlineData(PrintJobStatus.PrintedUnknown)]
    public async Task Lo_que_ya_salio_se_reimprime_y_queda_registrado_con_quien_y_por_que(PrintJobStatus estado)
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var jobId = await SembrarAsync(setup.Db, estado, ippJobId: 41);

        var r = await Servicio(setup.Db).ReprintAsync(jobId, "MAL_IMPRESO", "  salió en blanco ", Jefe);

        Assert.True(r.Success);
        setup.Db.ChangeTracker.Clear();
        var job = await setup.Db.PrintJobs.SingleAsync();
        Assert.Equal(PrintJobStatus.Routed, job.Status);
        Assert.Null(job.IppJobId); // el número era del envío anterior: no debe confirmar la copia nueva
        var ev = await setup.Db.PrintJobEvents.SingleAsync(e => e.EventType == RoutingService.ReprintEvent);
        Assert.Equal(estado, ev.OldStatus);
        Assert.Equal(Jefe, ev.ActorId);
        Assert.Contains("Mal impreso (salió en blanco)", ev.Message);
        using var meta = JsonDocument.Parse(ev.MetadataJson!);
        Assert.Equal("MAL_IMPRESO", meta.RootElement.GetProperty("reason").GetString());
        Assert.Equal("salió en blanco", meta.RootElement.GetProperty("note").GetString());
        Assert.Equal(10, meta.RootElement.GetProperty("printerId").GetInt32());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("INVENTADO", null)]
    [InlineData("OTRO", null)]   // «Otro» sin explicar
    [InlineData("OTRO", "   ")]
    public async Task Sin_motivo_valido_no_se_reimprime(string? motivo, string? nota)
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var jobId = await SembrarAsync(setup.Db, PrintJobStatus.PrintedConfirmed);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(setup.Db).ReprintAsync(jobId, motivo, nota, Jefe));
        Assert.Equal(PrintJobStatus.PrintedConfirmed, (await setup.Db.PrintJobs.SingleAsync()).Status);
        Assert.Empty(await setup.Db.PrintJobEvents.ToListAsync());
    }

    [Theory]
    [InlineData(PrintJobStatus.ErrorFinal)]     // no salió: eso es reintentar
    [InlineData(PrintJobStatus.SpoolAccepted)]  // salió hace nada y espera confirmación
    [InlineData(PrintJobStatus.Pending)]
    public async Task Solo_se_reimprime_lo_que_ya_salio(PrintJobStatus estado)
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var jobId = await SembrarAsync(setup.Db, estado);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(setup.Db).ReprintAsync(jobId, "PERDIDO", null, Jefe));
    }

    [Fact]
    public async Task Sin_PDF_por_la_retencion_no_se_reimprime_y_lo_dice()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var jobId = await SembrarAsync(setup.Db, PrintJobStatus.PrintedConfirmed, pdf: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(setup.Db).ReprintAsync(jobId, "PERDIDO", null, Jefe));
        Assert.Contains("ERP", ex.Message);
    }

    [Theory]
    [InlineData(PrintJobStatus.PrintedConfirmed)]
    [InlineData(PrintJobStatus.PrintedUnknown)]
    public async Task Por_el_reintento_no_se_cuela_ninguna_copia_sin_motivo(PrintJobStatus estado)
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var jobId = await SembrarAsync(setup.Db, estado);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(setup.Db).TryRetryRouteAsync(jobId, default, Jefe));
        Assert.Equal(estado, (await setup.Db.PrintJobs.SingleAsync()).Status);
    }

    // Sin PDF (descarga fallida o retención) acabaría en PDF_MISSING: se dice antes (auditoría L4).
    [Fact]
    public async Task Un_error_sin_PDF_no_se_reintenta_y_lo_dice()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var jobId = await SembrarAsync(setup.Db, PrintJobStatus.ErrorFinal, pdf: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(setup.Db).TryRetryRouteAsync(jobId, default, Jefe));
        Assert.Contains("ERP", ex.Message);
        Assert.Equal(PrintJobStatus.ErrorFinal, (await setup.Db.PrintJobs.SingleAsync()).Status);
        Assert.Empty(await setup.Db.PrintJobEvents.ToListAsync());
    }

    // Abierto desde el KPI, el registro da las mismas: el periodo del dashboard y solo tiendas activas (L8).
    [Fact]
    public async Task Con_el_periodo_del_dashboard_el_registro_cuadra_con_su_KPI()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var reciente = await SembrarAsync(setup.Db, PrintJobStatus.PrintedConfirmed, tienda: 1);
        var antigua = await SembrarAsync(setup.Db, PrintJobStatus.PrintedConfirmed, tienda: 1);
        var cerrada = await SembrarAsync(setup.Db, PrintJobStatus.PrintedConfirmed, tienda: 2);
        foreach (var id in new[] { reciente, antigua, cerrada })
            await Servicio(setup.Db).ReprintAsync(id, "PERDIDO", null, Jefe);
        var haceOchoDias = DateTimeOffset.UtcNow.AddDays(-8);
        await setup.Db.PrintJobEvents.Where(e => e.JobId == antigua)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.OccurredAtUtc, haceOchoDias));
        await setup.Db.Stores.Where(s => s.StoreId == 2).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));

        var filas = Filas(await Controlador(setup.Db, "Admin", null).GetReprints(null, null, null, null, "7d", null, null, default));

        Assert.Equal(reciente, Assert.Single(filas).GetProperty("jobId").GetGuid());
    }

    [Fact]
    public async Task El_reintento_tras_un_error_apunta_quien_lo_pidio()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var jobId = await SembrarAsync(setup.Db, PrintJobStatus.ErrorFinal);

        await Servicio(setup.Db).TryRetryRouteAsync(jobId, default, Jefe);

        var ev = await setup.Db.PrintJobEvents.SingleAsync(e => e.EventType == RoutingService.RetryRouteEvent);
        Assert.Equal(Jefe, ev.ActorId);
    }

    [Fact]
    public async Task El_registro_da_quien_cuando_motivo_e_impresora_y_cada_jefe_ve_solo_su_tienda()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var mia = await SembrarAsync(setup.Db, PrintJobStatus.PrintedConfirmed, tienda: 1);
        var otra = await SembrarAsync(setup.Db, PrintJobStatus.PrintedConfirmed, tienda: 2);
        await Servicio(setup.Db).ReprintAsync(mia, "DANADO", null, Jefe);
        await Servicio(setup.Db).ReprintAsync(otra, "PERDIDO", null, "ERP 9 Otro jefe");

        var jefe = Controlador(setup.Db, rol: "StoreManager", tienda: "1");
        var filas = Filas(await jefe.GetReprints(null, null, null, null, null, null, null, default));
        var fila = Assert.Single(filas);
        Assert.Equal("Dañado o mojado", fila.GetProperty("reasonText").GetString());
        Assert.Equal(Jefe, fila.GetProperty("actor").GetString());
        Assert.Equal("P1", fila.GetProperty("printerName").GetString());

        var admin = Controlador(setup.Db, rol: "Admin", tienda: null);
        Assert.Equal(2, Filas(await admin.GetReprints(null, null, null, null, null, null, null, default)).Count);
    }

    /// <summary>
    /// Con page, por páginas (WEB-2), filtrando por motivo y con el recuento de cada motivo del periodo
    /// entero, que la pantalla enseña aunque se filtre.
    /// </summary>
    [Fact]
    public async Task El_registro_pagina_filtra_por_motivo_y_cuenta_cada_motivo()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        foreach (var motivo in new[] { "PERDIDO", "PERDIDO", "PERDIDO", "DANADO" })
            await Servicio(setup.Db).ReprintAsync(
                await SembrarAsync(setup.Db, PrintJobStatus.PrintedConfirmed), motivo, null, Jefe);
        var admin = Controlador(setup.Db, "Admin", null);

        var pagina = Pagina(await admin.GetReprints(null, null, null, 2, null, 2, "PERDIDO", default));
        Assert.Equal(3, pagina.GetProperty("count").GetInt32());
        Assert.Equal(2, pagina.GetProperty("page").GetInt32());
        Assert.Single(pagina.GetProperty("value").EnumerateArray());
        Assert.Equal(3, pagina.GetProperty("byReason").GetProperty("PERDIDO").GetInt32());
        Assert.Equal(1, pagina.GetProperty("byReason").GetProperty("DANADO").GetInt32());

        Assert.IsType<BadRequestObjectResult>(await admin.GetReprints(null, null, null, null, null, 1, "%", default));
    }

    [Fact]
    public async Task Reimprimir_desde_la_API_fuera_de_su_tienda_esta_prohibido()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var otra = await SembrarAsync(setup.Db, PrintJobStatus.PrintedConfirmed, tienda: 2);

        var jefe = Controlador(setup.Db, rol: "StoreManager", tienda: "1");
        Assert.IsType<ForbidResult>(await jefe.Reprint(otra, new ReprintRequest("PERDIDO", null), default));
        Assert.IsType<BadRequestObjectResult>(
            await Controlador(setup.Db, "Admin", null).Reprint(otra, new ReprintRequest(null, null), default));
    }

    private static RoutingService Servicio(ImpresorasDbContext db) => new(db, new RoutingResolver(db));

    private static PrintJobsController Controlador(ImpresorasDbContext db, string rol, string? tienda)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, Jefe), new(ClaimTypes.Role, rol) };
        if (tienda is not null)
            claims.Add(new Claim("StoreId", tienda));
        return new PrintJobsController(db, Servicio(db), TimeProvider.System)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
            }
        };
    }

    private static JsonElement Pagina(IActionResult r)
        => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(r).Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static List<JsonElement> Filas(IActionResult r)
        => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(r).Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).EnumerateArray().ToList();

    private static async Task<Guid> SembrarAsync(
        ImpresorasDbContext db, PrintJobStatus estado, int tienda = 1, bool pdf = true, int? ippJobId = null)
    {
        var now = DateTimeOffset.UtcNow;
        if (!await db.Stores.AnyAsync(s => s.StoreId == tienda))
        {
            db.Stores.Add(new Store { StoreId = tienda, Name = $"Tienda {tienda}", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.Printers.Add(new Printer
            {
                PrinterId = tienda * 10, PrinterName = $"P{tienda}", SpoolQueue = $"COLA{tienda}", StoreId = tienda,
                IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now
            });
            db.RoutingRules.Add(new RoutingRule
            {
                Priority = 1, StoreId = tienda, PrinterId = tienda * 10, IsActive = true,
                ValidFromUtc = now.AddMinutes(-1), CreatedBy = "test", CreatedAtUtc = now, UpdatedAtUtc = now
            });
        }
        var job = new PrintJob
        {
            JobId = Guid.NewGuid(), SourceSystem = "ERP", ExternalJobId = $"A-{Guid.NewGuid():N}", StoreId = tienda,
            DocumentType = "A", Channel = "DEFAULT", PdfBlob = pdf ? MinimalPdf.Bytes : null, PdfSha256 = "sha",
            Status = estado, PrinterId = tienda * 10, IppJobId = ippJobId, CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return job.JobId;
    }
}
