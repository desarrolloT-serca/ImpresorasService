using System.Net.Http.Json;
using System.Text.Json;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

/// <summary>
/// Fixtures de F1.4 (docs/roadmap-kpi-dashboard.md): validan la semántica de ventana temporal
/// del overview (F1.1 evento vs cohorte, F1.2 timezone de negocio, F1.3 tiendas inactivas)
/// con un reloj controlado, tal como se especificó en el Reporte de Validación (§5).
/// </summary>
public sealed class DashboardControllerWindowTests : IClassFixture<WindowClockApiFactory>
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly WindowClockApiFactory _factory;
    private readonly HttpClient _client;

    public DashboardControllerWindowTests(WindowClockApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    [Fact]
    public async Task Fixture1_JobCreadoAyerImpresoHoy_CuentaComoImpresoHoy()
    {
        const int storeId = 991;
        // "ahora": 2026-01-15 10:00 Madrid (CET, UTC+1) -> today start = 2026-01-14T23:00:00Z
        var now = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
        // Creado ayer (Madrid) 18:00 CET = 17:00Z; impreso (UpdatedAtUtc) = ahora.
        var createdAtUtc = new DateTimeOffset(2026, 1, 14, 17, 0, 0, TimeSpan.Zero);

        await SeedStoreAsync(storeId);
        await ReplaceJobsAsync(storeId, MakeJob(storeId, PrintJobStatus.SpoolAccepted, createdAtUtc, now, attemptCount: 1));

        var kpis = await GetOverviewKpisAsync(storeId, now);

        Assert.Equal(0, kpis.Received); // no se creó hoy (Madrid)
        Assert.Equal(1, kpis.Printed);  // sí pasó a impreso hoy (Madrid) -> evento, no cohorte
    }

    [Fact]
    public async Task Fixture3_CruceMedianocheUtcPeroNoMadrid_CuentaComoRecibidoHoy()
    {
        const int storeId = 992;
        // "ahora": 2026-07-15 00:30 UTC = 02:30 CEST (verano, UTC+2) -> ya es "hoy" 15/7 en Madrid.
        var now = new DateTimeOffset(2026, 7, 15, 0, 30, 0, TimeSpan.Zero);
        // Creado 23:30 UTC del 14/7 = 01:30 CEST del 15/7: mismo día en Madrid, día distinto en UTC.
        var createdAtUtc = new DateTimeOffset(2026, 7, 14, 23, 30, 0, TimeSpan.Zero);

        await SeedStoreAsync(storeId);
        await ReplaceJobsAsync(storeId, MakeJob(storeId, PrintJobStatus.Pending, createdAtUtc, createdAtUtc));

        var kpis = await GetOverviewKpisAsync(storeId, now);

        Assert.Equal(1, kpis.Received); // con TZ Madrid cuenta; con TZ UTC/servidor habría dado 0
    }

    [Fact]
    public async Task Fixture4_ErrorFinalCreadoHaceDiasActualizadoHoy_CuentaEnFailedYFailedSinReintento()
    {
        const int storeId = 993;
        var now = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
        var createdAtUtc = now.AddDays(-10);

        await SeedStoreAsync(storeId);
        await ReplaceJobsAsync(storeId, MakeJob(storeId, PrintJobStatus.ErrorFinal, createdAtUtc, now, attemptCount: 3));

        var kpis = await GetOverviewKpisAsync(storeId, now);

        Assert.Equal(0, kpis.Received);                 // no creado en la ventana
        Assert.Equal(1, kpis.Failed);                    // sí actualizado (falló) en la ventana
        Assert.Equal(1, kpis.FailedWithoutRetryCurrent);  // coherente con Failed, misma ventana
    }

    /// <summary>
    /// A-KPI-01 (docs/auditoria-integral-2026-07-21.md): FailedWithoutRetryCurrent es una foto de
    /// estado, no un evento — un ErrorFinal es terminal y su UpdatedAtUtc no vuelve a moverse, así
    /// que filtrarlo por ventana lo hacía "desaparecer" al cruzar medianoche aunque el job siguiera
    /// roto (con el efecto de disparar una falsa alerta "RECUPERADA" en el Worker). Debe contar
    /// igual sin importar cuánto tiempo lleve el job en ese estado.
    /// </summary>
    [Fact]
    public async Task FailedWithoutRetryCurrent_DoesNotAgeOutOfWindow_ErrorFinalFromYesterdayStillCountsToday()
    {
        const int storeId = 1003;
        // "ahora": 2026-01-15 09:00 UTC (today start Madrid = 2026-01-14T23:00:00Z).
        var now = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
        // ErrorFinal ocurrido ayer, muy antes del inicio de "hoy" en Madrid.
        var failedYesterday = new DateTimeOffset(2026, 1, 13, 10, 0, 0, TimeSpan.Zero);

        await SeedStoreAsync(storeId);
        await ReplaceJobsAsync(storeId, MakeJob(storeId, PrintJobStatus.ErrorFinal, failedYesterday, failedYesterday, attemptCount: 3));

        var kpis = await GetOverviewKpisAsync(storeId, now);

        Assert.Equal(1, kpis.FailedWithoutRetryCurrent); // sigue roto hoy, aunque UpdatedAtUtc sea de anteayer
    }

    [Fact]
    public async Task Fixture7_JobDeTiendaInactiva_NoCuentaEnReceived()
    {
        const int storeId = 994;
        var now = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

        await SeedStoreAsync(storeId, isActive: false);
        await ReplaceJobsAsync(storeId, MakeJob(storeId, PrintJobStatus.Pending, now, now));

        var kpis = await GetOverviewKpisAsync(storeId, now);

        Assert.Equal(0, kpis.Received); // tienda inactiva excluida aunque se filtre por su storeId
    }

    [Fact]
    public async Task Fixture9_CreadoJustoAntesDeMedianocheMadrid_NoCuentaComoRecibidoHoy()
    {
        const int storeId = 995;
        // "ahora": 2026-01-21 00:05 CET (UTC+1) = 2026-01-20T23:05:00Z -> today start = 2026-01-20T23:00:00Z
        var now = new DateTimeOffset(2026, 1, 20, 23, 5, 0, TimeSpan.Zero);
        // Creado 2026-01-20 23:58 CET (día anterior en Madrid) = 22:58Z, antes del inicio de "hoy".
        var createdAtUtc = new DateTimeOffset(2026, 1, 20, 22, 58, 0, TimeSpan.Zero);

        await SeedStoreAsync(storeId);
        await ReplaceJobsAsync(storeId, MakeJob(storeId, PrintJobStatus.Pending, createdAtUtc, createdAtUtc));

        var kpis = await GetOverviewKpisAsync(storeId, now);

        Assert.Equal(0, kpis.Received); // el job pertenece al día local anterior, no a "hoy"
    }

    /// <summary>
    /// KPI-P1-001 (docs/prompt-roadmap-kpi-dashboard.md, Fase 0): "printed" hoy cuenta por
    /// UpdatedAtUtc + estado actual, no por evento de negocio. Un job que entra en SpoolAccepted
    /// el día 1 y el watchdog lo confirma el día 2 (misma transición física, dos actualizaciones
    /// de fila) se cuenta como "impreso" en AMBOS días. Este test reproduce el escenario completo
    /// con progresión temporal real (dos estados de BD, dos consultas) para probarlo, no solo
    /// para inspeccionar el código. Rojo hasta que Fase 1 cuente por primera transición a estado
    /// impreso (PrintJobEvents), no por snapshot actual.
    /// </summary>
    [Fact]
    public async Task KpiP1_001_Printed_DoesNotDoubleCount_WhenSpoolAcceptedYesterdayAndConfirmedToday()
    {
        const int storeId = 996;
        var day1 = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var day1EndOfDay = new DateTimeOffset(2026, 1, 15, 18, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 1, 16, 9, 0, 0, TimeSpan.Zero);

        await SeedStoreAsync(storeId);
        var jobId = Guid.NewGuid();

        // Día 1: el job entra en SpoolAccepted (primera vez que "se imprime").
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());
            db.PrintJobs.Add(new PrintJob
            {
                JobId = jobId,
                SourceSystem = "TEST",
                ExternalJobId = jobId.ToString("N"),
                StoreId = storeId,
                DocumentType = "FACTURA",
                Channel = "DEFAULT",
                PdfBlob = [0x25, 0x50, 0x44, 0x46],
                PdfSha256 = jobId.ToString("N"),
                Status = PrintJobStatus.SpoolAccepted,
                AttemptCount = 1,
                CorrelationId = Guid.NewGuid(),
                CreatedAtUtc = day1,
                UpdatedAtUtc = day1
            });
            db.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = jobId,
                EventType = "StatusChanged",
                OldStatus = PrintJobStatus.Printing,
                NewStatus = PrintJobStatus.SpoolAccepted,
                ActorType = "system",
                OccurredAtUtc = day1
            });
            await db.SaveChangesAsync();
        }

        var day1Kpis = await GetOverviewKpisAsync(storeId, day1EndOfDay);
        Assert.Equal(1, day1Kpis.Printed); // correcto: primera vez que se imprime, cuenta el día 1.

        // Día 2: el watchdog confirma la impresión (SpoolAccepted -> PrintedConfirmed). Es la
        // MISMA impresión física, no un trabajo nuevo.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            var job = await db.PrintJobs.SingleAsync(j => j.JobId == jobId);
            job.Status = PrintJobStatus.PrintedConfirmed;
            job.UpdatedAtUtc = day2;
            db.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = jobId,
                EventType = "StatusChanged",
                OldStatus = PrintJobStatus.SpoolAccepted,
                NewStatus = PrintJobStatus.PrintedConfirmed,
                ActorType = "system",
                OccurredAtUtc = day2
            });
            await db.SaveChangesAsync();
        }

        var day2Kpis = await GetOverviewKpisAsync(storeId, day2);
        Assert.Equal(0, day2Kpis.Printed); // ya se contó el día 1; la confirmación no es una nueva impresión.
    }

    [Fact]
    public async Task Printed_CountsFirstPrintedTransitionInsideWindow()
    {
        const int storeId = 997;
        var now = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

        await SeedStoreAsync(storeId);
        await ReplaceJobsAsync(storeId, MakeJob(storeId, PrintJobStatus.SpoolAccepted, now, now, attemptCount: 1));

        var kpis = await GetOverviewKpisAsync(storeId, now);

        Assert.Equal(1, kpis.Printed);
    }

    /// <summary>
    /// Prueba que "printed" lee exclusivamente PrintJobEvents, no Status/UpdatedAtUtc del job: el
    /// job está HOY en PrintedConfirmed (UpdatedAtUtc=hoy) pero su único evento de impresión ocurrió
    /// hace 5 días — no debe contar como impreso hoy.
    /// </summary>
    [Fact]
    public async Task Printed_DoesNotCountFirstPrintedTransitionBeforeWindowEvenIfUpdatedToday()
    {
        const int storeId = 998;
        var now = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
        var fiveDaysAgo = now.AddDays(-5);

        await SeedStoreAsync(storeId);
        var jobId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());
            var job = MakeJob(storeId, PrintJobStatus.PrintedConfirmed, fiveDaysAgo, now, attemptCount: 1);
            job.JobId = jobId;
            db.PrintJobs.Add(job);
            db.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = jobId,
                EventType = "StatusChanged",
                NewStatus = PrintJobStatus.SpoolAccepted,
                ActorType = "system",
                OccurredAtUtc = fiveDaysAgo
            });
            await db.SaveChangesAsync();
        }

        var kpis = await GetOverviewKpisAsync(storeId, now);

        Assert.Equal(0, kpis.Printed);
    }

    [Fact]
    public async Task Failed_CountsFailureEventInsideWindow()
    {
        const int storeId = 999;
        var now = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

        await SeedStoreAsync(storeId);
        await ReplaceJobsAsync(storeId, MakeJob(storeId, PrintJobStatus.ErrorFinal, now, now, attemptCount: 1));

        var kpis = await GetOverviewKpisAsync(storeId, now);

        Assert.Equal(1, kpis.Failed);
    }

    /// <summary>
    /// Simétrico a Printed_DoesNotCountFirstPrintedTransitionBeforeWindow...: el job está HOY en
    /// ErrorFinal (UpdatedAtUtc=hoy) pero el evento de fallo ocurrió hace 5 días.
    /// </summary>
    [Fact]
    public async Task Failed_DoesNotMoveFailureToLaterWindowOnUnrelatedUpdate()
    {
        const int storeId = 1000;
        var now = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
        var fiveDaysAgo = now.AddDays(-5);

        await SeedStoreAsync(storeId);
        var jobId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());
            var job = MakeJob(storeId, PrintJobStatus.ErrorFinal, fiveDaysAgo, now, attemptCount: 1);
            job.JobId = jobId;
            db.PrintJobs.Add(job);
            db.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = jobId,
                EventType = "StatusChanged",
                NewStatus = PrintJobStatus.ErrorFinal,
                ActorType = "system",
                OccurredAtUtc = fiveDaysAgo
            });
            await db.SaveChangesAsync();
        }

        var kpis = await GetOverviewKpisAsync(storeId, now);

        Assert.Equal(0, kpis.Failed);
    }

    /// <summary>
    /// Invariante estructural: los KPIs globales deben ser exactamente la suma de las filas por
    /// tienda (mismo cálculo, sin doble camino de agregación). Consulta admin sin storeId (global).
    /// </summary>
    [Fact]
    public async Task Overview_StoreRowsSumToGlobalKpis()
    {
        var now = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
        const int storeA = 1001;
        const int storeB = 1002;

        await SeedStoreAsync(storeA);
        await SeedStoreAsync(storeB);
        await ReplaceJobsAsync(storeA,
            MakeJob(storeA, PrintJobStatus.SpoolAccepted, now, now, attemptCount: 1),
            MakeJob(storeA, PrintJobStatus.ErrorFinal, now, now, attemptCount: 1));
        await ReplaceJobsAsync(storeB,
            MakeJob(storeB, PrintJobStatus.Pending, now, now));

        _factory.Clock.SetUtcNow(now);
        var response = await _client.GetAsync("/api/dashboard/overview?window=today");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DashboardOverviewFullResponse>(JsonOptions);

        Assert.NotNull(body?.Kpis);
        Assert.NotNull(body?.Stores);
        Assert.Equal(body!.Kpis!.Received, body.Stores!.Sum(s => s.Received));
        Assert.Equal(body.Kpis.Printed, body.Stores.Sum(s => s.Printed));
        Assert.Equal(body.Kpis.Failed, body.Stores.Sum(s => s.Failed));
        Assert.Equal(body.Kpis.QueueCurrent, body.Stores.Sum(s => s.QueuedCurrent));
        Assert.Equal(body.Kpis.FailedWithoutRetryCurrent, body.Stores.Sum(s => s.FailedWithoutRetryCurrent));
    }

    private async Task<DashboardKpis> GetOverviewKpisAsync(int storeId, DateTimeOffset now)
    {
        _factory.Clock.SetUtcNow(now);

        var response = await _client.GetAsync($"/api/dashboard/overview?window=today&storeId={storeId}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<DashboardOverviewResponse>(JsonOptions);
        Assert.NotNull(body?.Kpis);
        return body!.Kpis!;
    }

    private async Task SeedStoreAsync(int storeId, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        var seedNow = DateTimeOffset.UtcNow;

        if (!await db.Stores.AnyAsync(s => s.StoreId == storeId))
        {
            db.Stores.Add(new Store
            {
                StoreId = storeId,
                Name = $"Window Test Store {storeId}",
                IsActive = isActive,
                CreatedAtUtc = seedNow,
                UpdatedAtUtc = seedNow
            });
            await db.SaveChangesAsync();
        }
    }

    private async Task ReplaceJobsAsync(int storeId, params PrintJob[] jobs)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

        db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());
        db.PrintJobs.AddRange(jobs);

        // "printed"/"failed" se calculan desde PrintJobEvents (KPI-P1-001/002), no solo desde el
        // Status/UpdatedAtUtc actual del job — sembrar el evento de transición junto a cada job.
        var eventStatuses = new[]
        {
            PrintJobStatus.SpoolAccepted, PrintJobStatus.PrintedConfirmed, PrintJobStatus.PrintedUnknown,
            PrintJobStatus.ErrorFinal, PrintJobStatus.RetryScheduled
        };
        db.PrintJobEvents.AddRange(jobs
            .Where(j => eventStatuses.Contains(j.Status))
            .Select(j => new PrintJobEvent
            {
                JobId = j.JobId,
                EventType = "StatusChanged",
                NewStatus = j.Status,
                ActorType = "system",
                OccurredAtUtc = j.UpdatedAtUtc
            }));

        await db.SaveChangesAsync();
    }

    private static PrintJob MakeJob(
        int storeId,
        PrintJobStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        int attemptCount = 0)
    {
        return new PrintJob
        {
            JobId = Guid.NewGuid(),
            SourceSystem = "TEST",
            ExternalJobId = Guid.NewGuid().ToString("N"),
            StoreId = storeId,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = [0x25, 0x50, 0x44, 0x46],
            PdfSha256 = Guid.NewGuid().ToString("N"),
            Status = status,
            AttemptCount = attemptCount,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc
        };
    }

    private sealed class DashboardOverviewResponse
    {
        public DashboardKpis? Kpis { get; set; }
    }

    private sealed class DashboardOverviewFullResponse
    {
        public DashboardKpis? Kpis { get; set; }
        public List<StoreSummaryRow>? Stores { get; set; }
    }

    private sealed class StoreSummaryRow
    {
        public int Received { get; set; }
        public int Printed { get; set; }
        public int Failed { get; set; }
        public int QueuedCurrent { get; set; }
        public int FailedWithoutRetryCurrent { get; set; }
    }

    private sealed class DashboardKpis
    {
        public int Received { get; set; }
        public int Printed { get; set; }
        public int Failed { get; set; }
        public int QueueCurrent { get; set; }
        public int FailedWithoutRetryCurrent { get; set; }
    }
}
