using System.Security.Claims;
using System.Text.Json;
using ImpresorasService.Api.Controllers;
using ImpresorasService.Api.Security;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Connectivity;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// Desvío a la impresora de reserva (ERP_SERCA docs/75). Primero la tabla de decisión de §5, una prueba por
/// fila; después el desvío contra la base, con la cancelación en la original de cada forma posible.
/// </summary>
public sealed class DesvioTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly ReglaDesvio Manual = ReglaDesvio.Default;

    // Leída por el monitor hace 20 s: dato fresco.
    private static Printer Impresora(int id, bool conectada = true, int? reserva = null, int tienda = 9,
        int? ippState = 3, string? motivos = null, DateTimeOffset? leida = null) => new()
    {
        PrinterId = id,
        PrinterName = $"P{id}",
        Host = $"192.0.2.{id}",
        StoreId = tienda,
        IsActive = true,
        BackupPrinterId = reserva,
        LastConnectionOk = conectada,
        LastConnectionCheckAtUtc = leida ?? Ahora.AddSeconds(-20),
        LastConnectionError = conectada ? null : PrinterConnectivityState.NoConnectionError,
        IppState = ippState,
        IppStateReasons = motivos,
        IppCheckedAtUtc = leida ?? Ahora.AddSeconds(-20),
        CreatedAtUtc = Ahora,
        UpdatedAtUtc = Ahora
    };

    private static PrintJob Trabajo(PrintJobStatus estado, int impresora = 1, string? error = null) => new()
    {
        JobId = Guid.NewGuid(),
        SourceSystem = "ERP",
        ExternalJobId = Guid.NewGuid().ToString("N")[..8],
        StoreId = 9,
        DocumentType = "A",
        PdfBlob = [1, 2, 3],
        PdfSha256 = "x",
        Status = estado,
        PrinterId = impresora,
        IppJobId = estado is PrintJobStatus.PrinterBlocked or PrintJobStatus.SpoolAccepted ? 160 : null,
        LastErrorCode = error,
        CorrelationId = Guid.NewGuid(),
        CreatedAtUtc = Ahora.AddMinutes(-3),
        UpdatedAtUtc = Ahora.AddMinutes(-3)
    };

    private static DesvioService.Evaluacion Evaluar(PrintJob job, Printer? original, Printer? reserva, ReglaDesvio? regla = null) =>
        DesvioService.Evaluar(job, original, reserva, regla ?? Manual, Ahora);

    #region Tabla de decisión (§5)

    [Theory]
    [InlineData(PrintJobStatus.Routed)]
    [InlineData(PrintJobStatus.RetryScheduled)]
    public void Fila1_SinEnviar_ConLaOriginalSinConexion_SeDesviaSinCancelar(PrintJobStatus estado)
    {
        var e = Evaluar(Trabajo(estado), Impresora(1, conectada: false, reserva: 2), Impresora(2));

        Assert.True(e.Posible);
        Assert.False(e.YaEnviado);
        Assert.Equal(2, e.ReservaId);
        Assert.Contains("sin conexión", e.Motivo);
    }

    [Fact]
    public void Fila1_ErrorFinal_SoloSiFuePorLaImpresora()
    {
        var original = Impresora(1, conectada: false, reserva: 2);
        Assert.True(Evaluar(Trabajo(PrintJobStatus.ErrorFinal, error: "PRINTER_UNREACHABLE"), original, Impresora(2)).Posible);
        Assert.False(Evaluar(Trabajo(PrintJobStatus.ErrorFinal, error: "ROUTE_NOT_FOUND"), original, Impresora(2)).Posible);
        Assert.False(Evaluar(Trabajo(PrintJobStatus.ErrorFinal, error: "PDF_MISSING"), original, Impresora(2)).Posible);
    }

    [Theory]
    [InlineData(3, null)]                          // libre
    [InlineData(4, null)]                          // imprimiendo
    [InlineData(5, "toner-low-warning")]          // parada, pero solo con avisos
    [InlineData(3, "media-empty-error")]          // un motivo de error sin estar parada
    public void Fila2_LaOriginalPuedeImprimir_NoSeDesvia(int estado, string? motivos)
    {
        var e = Evaluar(Trabajo(PrintJobStatus.Routed), Impresora(1, reserva: 2, ippState: estado, motivos: motivos), Impresora(2));

        Assert.False(e.Posible);
        Assert.Contains("puede imprimir", e.Motivo);
    }

    [Theory]
    [InlineData("media-empty-error")]
    [InlineData("media-jam-error, cover-open-error")]
    [InlineData("toner-empty-error")]
    public void Filas3a6_EnviadoYParadoPorError_SeDesviaCancelandoAntes(string motivos)
    {
        var e = Evaluar(Trabajo(PrintJobStatus.PrinterBlocked), Impresora(1, reserva: 2, ippState: 5, motivos: motivos), Impresora(2));

        Assert.True(e.Posible);
        Assert.True(e.YaEnviado);
    }

    [Fact]
    public void Fila7_SinReservaOConLaReservaQueTampocoPuede_NoSeDesvia()
    {
        var job = Trabajo(PrintJobStatus.Routed);
        Assert.Contains("no tiene impresora de reserva", Evaluar(job, Impresora(1, conectada: false), null).Motivo);
        Assert.Contains("tampoco sirve", Evaluar(job, Impresora(1, conectada: false, reserva: 2), Impresora(2, conectada: false)).Motivo);
        Assert.Contains("tampoco sirve", Evaluar(job, Impresora(1, conectada: false, reserva: 2),
            Impresora(2, ippState: 5, motivos: "media-jam-error")).Motivo);
        var desactivada = Impresora(2);
        desactivada.IsActive = false;
        Assert.False(Evaluar(job, Impresora(1, conectada: false, reserva: 2), desactivada).Posible);
        Assert.Contains("otra tienda", Evaluar(job, Impresora(1, conectada: false, reserva: 2), Impresora(2, tienda: 6)).Motivo);
    }

    [Theory]
    [InlineData(PrintJobStatus.PrintedUnknown)]
    [InlineData(PrintJobStatus.PrintedConfirmed)]
    public void Fila8_LoQuePudoSalir_SeReimprimeNoSeDesvia(PrintJobStatus estado)
    {
        var e = Evaluar(Trabajo(estado), Impresora(1, conectada: false, reserva: 2), Impresora(2));

        Assert.False(e.Posible);
        Assert.Contains("reimprime", e.Motivo);
    }

    [Fact]
    public void Fila9_UnSoloSalto()
    {
        var job = Trabajo(PrintJobStatus.Routed);
        job.DivertedFromPrinterId = 7;

        Assert.Contains("no se vuelve a desviar", Evaluar(job, Impresora(1, conectada: false, reserva: 2), Impresora(2)).Motivo);
    }

    [Fact]
    public void Fila10_SinDatoRecienteDeLaOriginal_NoSeDesvia()
    {
        var vieja = Impresora(1, conectada: false, reserva: 2, leida: Ahora.AddMinutes(-2));

        var e = Evaluar(Trabajo(PrintJobStatus.Routed), vieja, Impresora(2));

        Assert.False(e.Posible);
        Assert.Contains("dato reciente", e.Motivo);
    }

    [Fact]
    public void Fila11_ConElDesvioDesactivado_NoSeOfrece()
    {
        var e = Evaluar(Trabajo(PrintJobStatus.Routed), Impresora(1, conectada: false, reserva: 2), Impresora(2),
            ReglaDesvio.Default with { Modo = ReglaDesvio.Desactivado });

        Assert.False(e.Posible);
        Assert.Contains("desactivado", e.Motivo);
    }

    [Fact]
    public void LaVentanaRecibeElEstadoCortoDeCadaUnaYDesdeCuando()
    {
        var sinConexion = Impresora(1, conectada: false, reserva: 2);
        sinConexion.ConnectionFailingSinceUtc = Ahora.AddMinutes(-3);
        var e = Evaluar(Trabajo(PrintJobStatus.Routed), sinConexion, Impresora(2));
        Assert.Equal(("P1", "Sin conexión", Ahora.AddMinutes(-3), "Lista"),
            (e.OriginalNombre, e.OriginalEstado, e.OriginalDesde, e.ReservaEstado));

        var parada = Impresora(1, reserva: 2, ippState: 5, motivos: "media-empty-error");
        parada.IppStateSinceUtc = Ahora.AddMinutes(-1);
        var p = Evaluar(Trabajo(PrintJobStatus.PrinterBlocked), parada, Impresora(2));
        Assert.StartsWith("Parada", p.OriginalEstado);
        Assert.Equal(Ahora.AddMinutes(-1), p.OriginalDesde);
    }

    [Fact]
    public void SinHostConfigurado_NoEsSinConexion()
    {
        var sinHost = Impresora(1, conectada: false, reserva: 2);
        sinHost.LastConnectionError = PrinterConnectivityState.HostNotConfiguredError;

        Assert.Equal(DesvioService.Disponibilidad.Puede, DesvioService.Estado(sinHost, Ahora).Estado);
    }

    [Fact]
    public void EnviandoseAhora_NoSeDesvia()
    {
        Assert.Contains("enviando", Evaluar(Trabajo(PrintJobStatus.Printing), Impresora(1, conectada: false, reserva: 2), Impresora(2)).Motivo);
    }

    #endregion

    #region Desvío contra la base

    private sealed class ReglasFijas(ReglaDesvio desvio) : IDashboardThresholdRuleStore
    {
        public Task<ThresholdRuleSet> LoadAsync(CancellationToken ct) =>
            Task.FromResult(ThresholdRuleSet.Default with { Desvio = desvio });

        public Task<IReadOnlyList<string>> SaveAsync(ThresholdRuleSet rules, CancellationToken ct) => throw new NotImplementedException();
    }

    // La original contesta lo que se le diga al cancelar; alCancelar simula lo que pase mientras tanto.
    private sealed class IppFalso(CancelacionEnImpresora respuesta, Action? alCancelar = null) : IIppConfirmationService
    {
        public int Cancelaciones { get; private set; }

        public Task<CancelacionEnImpresora> CancelJobAsync(string printerHost, int jobId, string jobName, CancellationToken ct)
        {
            Cancelaciones++;
            alCancelar?.Invoke();
            return Task.FromResult(respuesta);
        }

        public Task<IppQueryResult> QueryPrinterStateAsync(string printerHost, CancellationToken ct) => throw new NotImplementedException();
        public Task<PrintSpoolResult?> PrintPdfAsync(string printerHost, byte[] pdf, string jobName, bool comoImagen, CancellationToken ct) => throw new NotImplementedException();
        public Task<IppQueryResult?> QueryJobStateAsync(string printerHost, int jobId, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class Reloj : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    // Original sin conexión o parada sin papel, con la reserva libre; y el trabajo.
    private static async Task<PrintJob> PrepararAsync(ImpresorasDbContext db, PrintJobStatus estado)
    {
        var parada = estado is PrintJobStatus.PrinterBlocked or PrintJobStatus.SpoolAccepted;
        db.Printers.Add(parada
            ? Impresora(1, reserva: 2, ippState: 5, motivos: "media-empty-error")
            : Impresora(1, conectada: false, reserva: 2));
        db.Printers.Add(Impresora(2));
        var job = Trabajo(estado);
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return job;
    }

    private static DesvioService Servicio(ImpresorasDbContext db, IIppConfirmationService? ipp = null, ReglaDesvio? regla = null) =>
        new(db, new ReglasFijas(regla ?? Manual), new Reloj(), ipp);

    private static async Task<(PrintJob Job, List<PrintJobEvent> Eventos)> LeerAsync(ImpresorasDbContext db, Guid id)
    {
        db.ChangeTracker.Clear();
        var job = await db.PrintJobs.AsNoTracking().SingleAsync(j => j.JobId == id);
        var eventos = await db.PrintJobEvents.AsNoTracking().Where(e => e.JobId == id).ToListAsync();
        return (job, eventos);
    }

    [Fact]
    public async Task SinEnviar_SeMandaALaReservaConSuEvento()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var job = await PrepararAsync(setup.Db, PrintJobStatus.RetryScheduled);
        var ipp = new IppFalso(CancelacionEnImpresora.Cancelado);

        var r = await Servicio(setup.Db, ipp).DesviarAsync(job.JobId, "ERP 123 Ana", aceptaDuplicado: false, CancellationToken.None);

        Assert.Equal("Desviado", r.Codigo);
        Assert.Equal(0, ipp.Cancelaciones); // no estaba en la original: nada que cancelar
        var (leido, eventos) = await LeerAsync(setup.Db, job.JobId);
        Assert.Equal(PrintJobStatus.Routed, leido.Status);
        Assert.Equal(2, leido.PrinterId);
        Assert.Equal(1, leido.DivertedFromPrinterId);
        Assert.Equal(0, leido.AttemptCount);
        Assert.NotNull(leido.LastRequeuedAtUtc);
        var evento = Assert.Single(eventos, e => e.EventType == DesvioService.DivertedEvent);
        Assert.Equal("ERP 123 Ana", evento.ActorId);
        Assert.Equal("Iba a «P1» (sin conexión): enviado a su reserva «P2».", evento.Message);
        using var meta = JsonDocument.Parse(evento.MetadataJson!);
        Assert.Equal(1, meta.RootElement.GetProperty("from").GetInt32());
        Assert.Equal(2, meta.RootElement.GetProperty("to").GetInt32());
        Assert.Equal("NoEnviado", meta.RootElement.GetProperty("cancelacion").GetString());
        Assert.False(meta.RootElement.GetProperty("aceptaDuplicado").GetBoolean());
    }

    [Theory]
    [InlineData(CancelacionEnImpresora.Cancelado)]
    [InlineData(CancelacionEnImpresora.YaNoSaldra)]
    public async Task Parado_ConCancelacionSegura_SeDesvia(CancelacionEnImpresora respuesta)
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var job = await PrepararAsync(setup.Db, PrintJobStatus.PrinterBlocked);
        var ipp = new IppFalso(respuesta);

        var r = await Servicio(setup.Db, ipp).DesviarAsync(job.JobId, "u", aceptaDuplicado: false, CancellationToken.None);

        Assert.Equal("Desviado", r.Codigo);
        Assert.Equal(1, ipp.Cancelaciones);
        var (leido, _) = await LeerAsync(setup.Db, job.JobId);
        Assert.Equal(2, leido.PrinterId);
        Assert.Null(leido.IppJobId); // el número era de la original
    }

    [Theory]
    [InlineData(CancelacionEnImpresora.SinRespuesta)]
    [InlineData(CancelacionEnImpresora.NoLoTiene)]
    [InlineData(CancelacionEnImpresora.NoEsNuestro)]
    [InlineData(CancelacionEnImpresora.SinNombre)]
    [InlineData(CancelacionEnImpresora.NoDeja)]
    public async Task Parado_ConCancelacionIncierta_SoloAceptandoElDuplicado(CancelacionEnImpresora respuesta)
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var job = await PrepararAsync(setup.Db, PrintJobStatus.PrinterBlocked);
        var servicio = Servicio(setup.Db, new IppFalso(respuesta));

        var sinAceptar = await servicio.DesviarAsync(job.JobId, "u", aceptaDuplicado: false, CancellationToken.None);

        Assert.Equal("Incierta", sinAceptar.Codigo);
        Assert.Contains("dos veces", sinAceptar.Mensaje);
        var (intacto, eventos) = await LeerAsync(setup.Db, job.JobId);
        Assert.Equal(PrintJobStatus.PrinterBlocked, intacto.Status);
        Assert.Equal(1, intacto.PrinterId);
        Assert.Empty(eventos);

        var aceptado = await servicio.DesviarAsync(job.JobId, "u", aceptaDuplicado: true, CancellationToken.None);

        Assert.Equal("Desviado", aceptado.Codigo);
        var (desviado, conEvento) = await LeerAsync(setup.Db, job.JobId);
        Assert.Equal(2, desviado.PrinterId);
        var evento = Assert.Single(conEvento);
        Assert.Contains("dos veces", evento.Message);
        using var meta = JsonDocument.Parse(evento.MetadataJson!);
        Assert.True(meta.RootElement.GetProperty("aceptaDuplicado").GetBoolean());
    }

    [Fact]
    public async Task Parado_SinNumeroDeTrabajo_EsIncierta()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var job = await PrepararAsync(setup.Db, PrintJobStatus.PrinterBlocked);
        await setup.Db.PrintJobs.Where(j => j.JobId == job.JobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.IppJobId, (int?)null));
        var ipp = new IppFalso(CancelacionEnImpresora.Cancelado);

        var r = await Servicio(setup.Db, ipp).DesviarAsync(job.JobId, "u", aceptaDuplicado: false, CancellationToken.None);

        Assert.Equal("Incierta", r.Codigo);
        Assert.Equal(0, ipp.Cancelaciones);
    }

    [Fact]
    public async Task Parado_YLaOriginalDiceQueYaSalio_QuedaImpresoYNoSeDesvia()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var job = await PrepararAsync(setup.Db, PrintJobStatus.PrinterBlocked);

        var r = await Servicio(setup.Db, new IppFalso(CancelacionEnImpresora.YaSalio))
            .DesviarAsync(job.JobId, "u", aceptaDuplicado: true, CancellationToken.None);

        Assert.Equal("YaSalio", r.Codigo);
        var (leido, eventos) = await LeerAsync(setup.Db, job.JobId);
        Assert.Equal(PrintJobStatus.PrintedConfirmed, leido.Status);
        Assert.Equal(1, leido.PrinterId);
        Assert.Null(leido.DivertedFromPrinterId);
        Assert.Equal("PRINTED_BEFORE_DIVERT", Assert.Single(eventos).EventType);
    }

    [Fact]
    public async Task SiElWorkerLoCambiaMientrasSeCancela_ConflictoYNoSeDesvia()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var job = await PrepararAsync(setup.Db, PrintJobStatus.PrinterBlocked);
        // Mientras se cancela, el vigilante lo pasa a «sin confirmar».
        var ipp = new IppFalso(CancelacionEnImpresora.Cancelado, () =>
            setup.Db.PrintJobs.Where(j => j.JobId == job.JobId)
                .ExecuteUpdate(s => s.SetProperty(j => j.Status, PrintJobStatus.PrintedUnknown)));

        await Assert.ThrowsAsync<PrintJobStateConflictException>(() =>
            Servicio(setup.Db, ipp).DesviarAsync(job.JobId, "u", aceptaDuplicado: false, CancellationToken.None));

        var (leido, eventos) = await LeerAsync(setup.Db, job.JobId);
        Assert.Equal(1, leido.PrinterId);
        Assert.Empty(eventos);
    }

    [Fact]
    public async Task LoQueLaTablaNoPermite_SeRechazaConElMotivo()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var job = await PrepararAsync(setup.Db, PrintJobStatus.RetryScheduled);

        var desactivado = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio(setup.Db, regla: Manual with { Modo = ReglaDesvio.Desactivado })
                .DesviarAsync(job.JobId, "u", aceptaDuplicado: false, CancellationToken.None));
        Assert.Contains("desactivado", desactivado.Message);

        // Desviado una vez, no se vuelve a desviar.
        await Servicio(setup.Db).DesviarAsync(job.JobId, "u", aceptaDuplicado: false, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio(setup.Db).DesviarAsync(job.JobId, "u", aceptaDuplicado: false, CancellationToken.None));
    }

    [Fact]
    public async Task SinPdf_NoSeDesvia()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var job = await PrepararAsync(setup.Db, PrintJobStatus.RetryScheduled);
        await setup.Db.PrintJobs.Where(j => j.JobId == job.JobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.PdfBlob, (byte[]?)null));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio(setup.Db).DesviarAsync(job.JobId, "u", aceptaDuplicado: false, CancellationToken.None));

        Assert.Contains("PDF", ex.Message);
    }

    #endregion

    #region Regla en «Reglas y avisos»

    [Fact]
    public void Regla_SoloModosConocidosYEsperaDe1a9()
    {
        Assert.Empty(ThresholdRuleEngine.Validate(ThresholdRuleSet.Default));
        Assert.NotEmpty(ThresholdRuleEngine.Validate(ThresholdRuleSet.Default with { Desvio = new ReglaDesvio("automatico", 2) }));
        Assert.NotEmpty(ThresholdRuleEngine.Validate(ThresholdRuleSet.Default with { Desvio = new ReglaDesvio("manual", 0) }));
        Assert.NotEmpty(ThresholdRuleEngine.Validate(ThresholdRuleSet.Default with { Desvio = new ReglaDesvio("manual", 10) }));
    }

    [Fact]
    public async Task Regla_UnFicheroAnteriorDaManual_YSeGuardaYSeLee()
    {
        var fichero = Path.Combine(Path.GetTempPath(), $"reglas-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(fichero, """{"queue":[{"min":5,"severity":"warning"}],"failed":[],"missingHost":[],"conn":[]}""");
            var almacen = new DashboardThresholdRuleStore(Microsoft.Extensions.Options.Options.Create(
                new ImpresorasService.Infrastructure.Options.DashboardThresholdRulesOptions { ThresholdRulesFilePath = fichero }));

            Assert.Equal(ReglaDesvio.Default, (await almacen.LoadAsync(CancellationToken.None)).Desvio);

            Assert.Empty(await almacen.SaveAsync(
                ThresholdRuleSet.Default with { Desvio = new ReglaDesvio(" Desactivado ", 3) }, CancellationToken.None));
            Assert.Equal(new ReglaDesvio("desactivado", 3), (await almacen.LoadAsync(CancellationToken.None)).Desvio);
        }
        finally
        {
            File.Delete(fichero);
        }
    }

    #endregion

    #region Rutas GET y POST /api/printjobs/{id}/divert

    private static PrintJobsController Controlador(ImpresorasDbContext db, string rol, string tienda, bool compartido = false)
    {
        var controlador = new PrintJobsController(db, new RoutingService(db, new RoutingResolver(db)), new Reloj());
        controlador.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Role, rol), new Claim("StoreId", tienda),
                    new Claim(ClaimTypes.Name, "u"),
                    .. (compartido ? new[] { new Claim(PaseErp.CompartidoClaim, "si") } : Array.Empty<Claim>())
                ], "test"))
            }
        };
        return controlador;
    }

    [Fact]
    public async Task Rutas_FueraDeSuTienda_Prohibido()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var job = await PrepararAsync(setup.Db, PrintJobStatus.RetryScheduled);
        var controlador = Controlador(setup.Db, "StoreManager", "6");

        Assert.IsType<ForbidResult>(await controlador.DivertCheck(job.JobId, Servicio(setup.Db), CancellationToken.None));
        Assert.IsType<ForbidResult>(await controlador.Divert(job.JobId, null, Servicio(setup.Db), CancellationToken.None));
    }

    [Fact]
    public async Task Rutas_ComprobarNoCambiaNada_EInciertaPideConfirmarSinDesviar()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var job = await PrepararAsync(setup.Db, PrintJobStatus.PrinterBlocked);
        var controlador = Controlador(setup.Db, "StoreManager", "9");
        var servicio = Servicio(setup.Db, new IppFalso(CancelacionEnImpresora.SinRespuesta));

        var comprobado = Assert.IsType<OkObjectResult>(await controlador.DivertCheck(job.JobId, servicio, CancellationToken.None));
        Assert.Contains("\"Posible\":true", JsonSerializer.Serialize(comprobado.Value));
        var incierta = Assert.IsType<OkObjectResult>(
            await controlador.Divert(job.JobId, new DivertRequest(), servicio, CancellationToken.None));
        Assert.Contains("\"codigo\":\"Incierta\"", JsonSerializer.Serialize(incierta.Value));

        var (leido, _) = await LeerAsync(setup.Db, job.JobId);
        Assert.Equal(1, leido.PrinterId);
    }

    [Fact]
    public async Task LaCola_DiceDeDondeVinoCuandoYQuien()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var job = await PrepararAsync(setup.Db, PrintJobStatus.RetryScheduled);
        await Servicio(setup.Db).DesviarAsync(job.JobId, "ERP 123 Ana", aceptaDuplicado: false, CancellationToken.None);
        var controlador = Controlador(setup.Db, "StoreManager", "9");

        var cola = Assert.IsType<OkObjectResult>(await controlador.GetQueue(
            null, null, null, null, null, null, null, null, null, null, null, null, null, CancellationToken.None));
        var fila = JsonSerializer.SerializeToElement(cola.Value)[0];

        Assert.Equal("P2", fila.GetProperty("PrinterName").GetString());
        Assert.Equal("P1", fila.GetProperty("DivertedFromName").GetString());
        Assert.Equal("Iba a «P1» (sin conexión): enviado a su reserva «P2».", fila.GetProperty("DivertedMessage").GetString());
        Assert.Equal("ERP 123 Ana", fila.GetProperty("DivertedBy").GetString());
        Assert.Equal(Ahora, fila.GetProperty("DivertedAtUtc").GetDateTimeOffset());
    }

    // 11/11/2026: el buscador por marca de la cola, que sustituye al registro de reimpresiones.
    [Fact]
    public async Task LaCola_FiltraPorMarcas()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var desviado = await PrepararAsync(setup.Db, PrintJobStatus.RetryScheduled);
        await Servicio(setup.Db).DesviarAsync(desviado.JobId, "u", aceptaDuplicado: false, CancellationToken.None);
        var copia = Trabajo(PrintJobStatus.PrintedConfirmed);
        var reenviado = Trabajo(PrintJobStatus.PrintedConfirmed);
        setup.Db.PrintJobs.AddRange(copia, reenviado);
        setup.Db.PrintJobEvents.AddRange(
            new PrintJobEvent { JobId = copia.JobId, EventType = RoutingService.ReprintEvent, OldStatus = PrintJobStatus.PrintedUnknown,
                NewStatus = PrintJobStatus.Pending, ActorType = "user", OccurredAtUtc = Ahora },
            new PrintJobEvent { JobId = reenviado.JobId, EventType = RoutingService.RetryRouteEvent, OldStatus = PrintJobStatus.ErrorFinal,
                NewStatus = PrintJobStatus.Pending, ActorType = "user", OccurredAtUtc = Ahora });
        await setup.Db.SaveChangesAsync();
        var controlador = Controlador(setup.Db, "StoreManager", "9");

        async Task<string[]> Con(string marcas) =>
            JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await controlador.GetQueue(
                    null, null, null, null, null, null, null, null, null, null, null, null, marcas, CancellationToken.None)).Value)
                .EnumerateArray().Select(f => f.GetProperty("ExternalJobId").GetString()!).Order().ToArray();

        Assert.Equal([desviado.ExternalJobId], await Con("reserva"));
        Assert.Equal(new[] { copia.ExternalJobId, reenviado.ExternalJobId }.Order(), await Con("copia,reintento"));
        Assert.Empty(await Con("reimpreso"));
        Assert.IsType<BadRequestObjectResult>(await controlador.GetQueue(
            null, null, null, null, null, null, null, null, null, null, null, null, "otra", CancellationToken.None));
    }

    // El detalle del trabajo: su historia; quién, el motivo y la nota de las reimpresiones, solo con permiso
    // de reimprimir con perfil personal: el perfil de tienda, compartido, reimprime pero no lo ve (11/11/2026).
    [Theory]
    [InlineData("StoreManager", false, true)]
    [InlineData("StoreManager", true, false)]
    [InlineData("Employee", false, false)]
    public async Task ElDetalle_EnsenaLaHistoria_YQuienSoloConPermiso(string rol, bool compartido, bool ve)
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var job = await PrepararAsync(setup.Db, PrintJobStatus.PrintedConfirmed);
        setup.Db.PrintJobEvents.Add(new PrintJobEvent
        {
            JobId = job.JobId, EventType = RoutingService.ReprintEvent, OldStatus = PrintJobStatus.PrintedConfirmed,
            NewStatus = PrintJobStatus.Pending, ActorType = "user", ActorId = "ERP 123 Ana",
            Message = "Reimpresión: Perdido (se mojó). Ya había salido: puede quedar duplicado.",
            MetadataJson = """{"reason":"PERDIDO","note":"se mojó","printerId":1}""", OccurredAtUtc = Ahora
        });
        await setup.Db.SaveChangesAsync();

        var detalle = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(
            await Controlador(setup.Db, rol, "9", compartido).GetDetail(job.JobId, CancellationToken.None)).Value);

        Assert.Equal("P1", detalle.GetProperty("PrinterName").GetString());
        Assert.Equal("P2", detalle.GetProperty("BackupPrinterName").GetString());
        var evento = detalle.GetProperty("Events")[0];
        Assert.Equal(ve ? "ERP 123 Ana" : null, evento.GetProperty("ActorId").GetString());
        Assert.Equal(ve ? "Perdido" : null, evento.GetProperty("Reason").GetString());
        Assert.Equal(ve ? "se mojó" : null, evento.GetProperty("Note").GetString());
        Assert.Equal(!ve, evento.GetProperty("Message").GetString()!.StartsWith("Reimpresión de algo"));
        // Fuera de su tienda, prohibido.
        Assert.IsType<ForbidResult>(await Controlador(setup.Db, rol, "6").GetDetail(job.JobId, CancellationToken.None));
        // Y el registro de quién reimprimió, lo mismo.
        var registro = await Controlador(setup.Db, rol, "9", compartido).GetReprints(
            null, null, null, null, null, null, null, CancellationToken.None);
        Assert.Equal(ve, registro is OkObjectResult);
    }

    // 11/11/2026: la columna «Marcas» separa la reimpresión de algo sin confirmar (posible copia) de la de
    // algo que sí había salido.
    [Fact]
    public async Task LaCola_CuentaAparteLasReimpresionesDeLoSinConfirmar()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var job = await PrepararAsync(setup.Db, PrintJobStatus.PrintedConfirmed);
        foreach (var antes in new[] { PrintJobStatus.PrintedUnknown, PrintJobStatus.PrintedConfirmed, PrintJobStatus.PrintedUnknown })
            setup.Db.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = job.JobId, EventType = RoutingService.ReprintEvent, OldStatus = antes,
                NewStatus = PrintJobStatus.Pending, ActorType = "user", OccurredAtUtc = Ahora
            });
        await setup.Db.SaveChangesAsync();

        var cola = Assert.IsType<OkObjectResult>(await Controlador(setup.Db, "StoreManager", "9").GetQueue(
            null, null, null, null, null, null, null, null, null, null, null, null, null, CancellationToken.None));
        var fila = JsonSerializer.SerializeToElement(cola.Value)[0];

        Assert.Equal(3, fila.GetProperty("Reprints").GetInt32());
        Assert.Equal(2, fila.GetProperty("ReprintsUnconfirmed").GetInt32());
    }

    #endregion
}
