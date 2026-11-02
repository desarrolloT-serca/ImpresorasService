using System.Net;
using System.Net.Http.Json;
using ImpresorasService.Api.IntegrationTests;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

/// <summary>
/// Tests de integración para PrintJobs y enrutado.
/// </summary>
public sealed class PrintJobsControllerTests : IntegrationTestBase
{
    public PrintJobsControllerTests(ApiWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetQueue_WhenEmpty_Returns200AndEmptyArray()
    {
        await Factory.ResetDatabaseAsync();
        var response = await Client.GetAsync("/api/printjobs");

        response.EnsureSuccessStatusCode();
        var jobs = await response.Content.ReadFromJsonAsync<List<object>>();
        Assert.NotNull(jobs);
        Assert.Empty(jobs);
    }

    [Fact]
    public async Task GetQueue_SeparaReintentoTrasErrorDeReimpresionYDaLaHoraDelUltimoEnvio()
    {
        await Factory.ResetDatabaseAsync();
        var jobId = await Factory.SeedPendingPrintJobAsync();
        var t0 = DateTimeOffset.UtcNow.AddHours(-3);
        await AñadirEventosAsync(jobId,
            Evento(jobId, "StatusChanged", null, PrintJobStatus.ErrorFinal, t0),
            Evento(jobId, RoutingService.RetryRouteEvent, PrintJobStatus.ErrorFinal, PrintJobStatus.Pending, t0.AddMinutes(10)),
            Evento(jobId, "StatusChanged", PrintJobStatus.Printing, PrintJobStatus.SpoolAccepted, t0.AddHours(1)),
            Evento(jobId, "StatusChanged", null, PrintJobStatus.PrintedUnknown, t0.AddHours(1).AddMinutes(2)),
            Evento(jobId, RoutingService.ReprintEvent, PrintJobStatus.PrintedUnknown, PrintJobStatus.Pending, t0.AddHours(2)),
            Evento(jobId, "StatusChanged", PrintJobStatus.Printing, PrintJobStatus.SpoolAccepted, t0.AddHours(2).AddMinutes(1)),
            // Se quedó sin papel y se recuperó: no es otro envío (auditoría L2).
            Evento(jobId, "StatusChanged", PrintJobStatus.SpoolAccepted, PrintJobStatus.PrinterBlocked, t0.AddHours(2).AddMinutes(5)),
            Evento(jobId, "StatusChanged", PrintJobStatus.PrinterBlocked, PrintJobStatus.SpoolAccepted, t0.AddHours(2).AddMinutes(20)));

        var job = Assert.Single((await Client.GetFromJsonAsync<List<TrabajoEnCola>>("/api/printjobs"))!);

        // SQLite (tests) guarda la fecha sin fracciones de segundo; el envío anterior fue 1 h antes.
        Assert.Equal(t0.AddHours(2).AddMinutes(1), job.SentAtUtc!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(1, job.Retries);
        Assert.Equal(1, job.Reprints);
    }

    [Fact]
    public async Task GetQueue_ReintentadoQueAunNoHaVueltoASalir_NoTieneHoraDeEnvio()
    {
        await Factory.ResetDatabaseAsync();
        var jobId = await Factory.SeedPendingPrintJobAsync();
        var t0 = DateTimeOffset.UtcNow.AddHours(-1);
        await AñadirEventosAsync(jobId,
            Evento(jobId, "StatusChanged", PrintJobStatus.Printing, PrintJobStatus.SpoolAccepted, t0),
            Evento(jobId, RoutingService.ReprintEvent, PrintJobStatus.PrintedUnknown, PrintJobStatus.Pending, t0.AddMinutes(30)));

        var job = Assert.Single((await Client.GetFromJsonAsync<List<TrabajoEnCola>>("/api/printjobs"))!);

        Assert.Null(job.SentAtUtc); // el envío de t0 es de antes de la reimpresión
        Assert.Equal(1, job.Reprints);
    }

    private static PrintJobEvent Evento(Guid jobId, string tipo, PrintJobStatus? antes, PrintJobStatus? despues, DateTimeOffset cuando) => new()
    {
        JobId = jobId, EventType = tipo, OldStatus = antes, NewStatus = despues, ActorType = "system", OccurredAtUtc = cuando
    };

    private async Task AñadirEventosAsync(Guid jobId, params PrintJobEvent[] eventos)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        db.PrintJobEvents.AddRange(eventos);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetQueue_SinEnviar_NoTieneHoraDeEnvio()
    {
        await Factory.ResetDatabaseAsync();
        await Factory.SeedPendingPrintJobAsync();

        var job = Assert.Single((await Client.GetFromJsonAsync<List<TrabajoEnCola>>("/api/printjobs"))!);

        Assert.Null(job.SentAtUtc);
        Assert.Equal(0, job.Retries);
        Assert.Equal(0, job.Reprints);
    }

    private sealed record TrabajoEnCola(DateTimeOffset? SentAtUtc, int Retries, int Reprints);

    /// <summary>
    /// La cola ordena por la última vez que se mandó imprimir, no por la llegada: lo reintentado
    /// sube arriba aunque vuelva a fallar, y no se pierde entre lo que llegó después (27/10/2026).
    /// </summary>
    [Fact]
    public async Task GetQueue_OrdenaPorLaUltimaVezQueSeMandoImprimir()
    {
        await Factory.ResetDatabaseAsync();
        var viejo = await CreatePendingJobAsync();
        var nuevo = await CreatePendingJobAsync();
        // Sin reglas: los dos acaban en ErrorFinal (ROUTE_NOT_FOUND).
        foreach (var id in new[] { viejo, nuevo })
            (await Client.PostAsync($"/api/printjobs/{id}/route", null)).EnsureSuccessStatusCode();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            (await db.PrintJobs.FindAsync(viejo))!.CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-2);
            (await db.PrintJobs.FindAsync(nuevo))!.CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(nuevo, (await Client.GetFromJsonAsync<List<FilaCola>>("/api/printjobs"))![0].JobId);

        (await Client.PostAsync($"/api/printjobs/{viejo}/route", null)).EnsureSuccessStatusCode();

        var cola = (await Client.GetFromJsonAsync<List<FilaCola>>("/api/printjobs"))!;
        Assert.Equal([viejo, nuevo], cola.Select(x => x.JobId));
        Assert.NotNull(cola[0].LastRequeuedAtUtc);
        Assert.Null(cola[1].LastRequeuedAtUtc);
    }

    private sealed record FilaCola(Guid JobId, DateTimeOffset? LastRequeuedAtUtc);

    /// <summary>
    /// Búsqueda avanzada de la cola: fechas (días enteros de la zona de negocio, sobre la última vez
    /// que se mandó imprimir), varias impresoras y varios tipos de documento, combinables.
    /// </summary>
    [Fact]
    public async Task GetQueue_FiltraPorFechasImpresorasYTipos()
    {
        await Factory.ResetDatabaseAsync();
        var p1 = (await CreatePrinterAsync()).PrinterId;
        var p2 = (await CreatePrinterAsync()).PrinterId;
        var antiguo = await Factory.SeedPendingPrintJobAsync(documentType: "A");
        var nuevo = await Factory.SeedPendingPrintJobAsync(documentType: "C");
        var reimpreso = await Factory.SeedPendingPrintJobAsync(documentType: "A");
        var hace3Dias = DateTimeOffset.UtcNow.AddDays(-3);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            foreach (var (id, impresora) in new[] { (antiguo, p1), (nuevo, p2), (reimpreso, p1) })
                (await db.PrintJobs.FindAsync(id))!.PrinterId = impresora;
            (await db.PrintJobs.FindAsync(antiguo))!.CreatedAtUtc = hace3Dias;
            var r = (await db.PrintJobs.FindAsync(reimpreso))!;
            r.CreatedAtUtc = hace3Dias;
            r.LastRequeuedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        var zona = BusinessTimeZoneClock.Resolve(Factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Dashboard:BusinessTimeZone"]);
        var dia = (DateTimeOffset f) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(f, zona).Date).ToString("yyyy-MM-dd");
        async Task<Guid[]> Cola(string filtro) =>
            (await Client.GetFromJsonAsync<List<FilaCola>>($"/api/printjobs?{filtro}"))!.Select(x => x.JobId).Order().ToArray();
        Guid[] Ids(params Guid[] ids) => ids.Order().ToArray();

        Assert.Equal(Ids(nuevo, reimpreso), await Cola($"fromDate={dia(DateTimeOffset.UtcNow)}"));
        Assert.Equal(Ids(antiguo), await Cola($"toDate={dia(hace3Dias)}"));
        Assert.Equal(Ids(antiguo, reimpreso), await Cola($"printerIds={p1}"));
        Assert.Equal(Ids(nuevo), await Cola("documentTypes=C"));
        Assert.Equal(Ids(nuevo), await Cola($"documentTypes=A,C&printerIds={p2}"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync("/api/printjobs?printerIds=1,x")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync("/api/printjobs?fromDate=2026-10-27&toDate=2026-10-26")).StatusCode);
    }

    [Fact]
    public async Task Route_WhenJobNotExists_Returns404()
    {
        var response = await Client.PostAsync("/api/printjobs/00000000-0000-0000-0000-000000000001/route", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Route_WhenNoRule_Returns200WithErrorFinal()
    {
        await Factory.ResetDatabaseAsync();
        var jobId = await CreatePendingJobAsync();

        var response = await Client.PostAsync($"/api/printjobs/{jobId}/route", null);

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<RouteResponseDto>(response);
        Assert.NotNull(body);
        Assert.Equal("ErrorFinal", body.Status);
        Assert.Equal("ROUTE_NOT_FOUND", body.ErrorCode);
    }

    [Fact]
    public async Task Route_WhenRuleExists_Returns200WithRouted()
    {
        await Factory.ResetDatabaseAsync();
        var printer = await CreatePrinterAsync();
        await CreateGlobalRuleAsync(printer.PrinterId);
        var jobId = await CreatePendingJobAsync();

        var response = await Client.PostAsync($"/api/printjobs/{jobId}/route", null);

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<RouteResponseDto>(response);
        Assert.NotNull(body);
        Assert.Equal("Routed", body.Status);
        Assert.Equal(printer.PrinterId, body.PrinterId);
    }

    #region Helpers

    private async Task<PrinterDto> CreatePrinterAsync()
    {
        // Nombre propio: dos impresoras activas no pueden repetirlo en la tienda.
        var req = new { printerName = $"P-{Guid.NewGuid():N}"[..12], spoolQueue = $"\\\\srv\\{Guid.NewGuid():N}"[..30], storeId = 1, isActive = true };
        var res = await Client.PostAsJsonAsync("/api/printers", req);
        res.EnsureSuccessStatusCode();
        var p = await res.Content.ReadFromJsonAsync<PrinterDto>();
        Assert.NotNull(p);
        return p;
    }

    private async Task CreateGlobalRuleAsync(int printerId)
    {
        var req = new { priority = 10, storeId = (int?)null, printerId, isActive = true, createdBy = "test" };
        await Client.PostAsJsonAsync("/api/routingrules", req);
    }

    private async Task<Guid> CreatePendingJobAsync()
    {
        return await Factory.SeedPendingPrintJobAsync();
    }

    private sealed record PrinterDto(int PrinterId);
    private sealed record RouteResponseDto(string Status, int? PrinterId, string? ErrorCode);

    #endregion
}
