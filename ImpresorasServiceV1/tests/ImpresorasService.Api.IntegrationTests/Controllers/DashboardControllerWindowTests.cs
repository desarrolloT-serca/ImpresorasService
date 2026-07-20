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

    private sealed class DashboardKpis
    {
        public int Received { get; set; }
        public int Printed { get; set; }
        public int Failed { get; set; }
        public int QueueCurrent { get; set; }
        public int FailedWithoutRetryCurrent { get; set; }
    }
}
