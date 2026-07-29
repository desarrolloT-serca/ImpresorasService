using System.Net.Http.Json;
using System.Reflection;
using ImpresorasService.Api.Controllers;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

public sealed class DashboardControllerTests : IntegrationTestBase
{
    private const int DashboardTestStoreId = 880;

    public DashboardControllerTests(ApiWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetOverview_WithSeededData_ReturnsExpectedKpis()
    {
        await SeedDashboardScenarioAsync();

        var response = await Client.GetAsync($"/api/dashboard/overview?window=today&storeId={DashboardTestStoreId}");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<DashboardOverviewResponse>(response);
        Assert.NotNull(body);
        Assert.NotNull(body.Kpis);
        Assert.Equal(3, body.Kpis!.Received);
        Assert.Equal(1, body.Kpis.Printed);
        Assert.Equal(1, body.Kpis.Failed);
        Assert.Equal(1, body.Kpis.QueueCurrent);
        Assert.Equal(1, body.Kpis.FailedWithoutRetryCurrent);
        Assert.Equal(1, body.Kpis.ActivePrinters);
        Assert.Equal(1, body.Kpis.ActiveStores);
        Assert.NotNull(body.Stores);
        Assert.Single(body.Stores!);
        Assert.Equal(DashboardTestStoreId, body.Stores![0].StoreId);
        Assert.Equal("warning", body.Stores![0].Health);
    }

    [Fact]
    public async Task GetOverview_FilterByStoreId_ScopesKpis()
    {
        await SeedDashboardScenarioAsync();
        await SeedSecondStoreWithJobAsync();

        var response = await Client.GetAsync($"/api/dashboard/overview?window=today&storeId={DashboardTestStoreId}");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<DashboardOverviewResponse>(response);
        Assert.NotNull(body);
        Assert.Equal(3, body!.Kpis!.Received);
        Assert.Single(body.Stores!);
        Assert.Equal(DashboardTestStoreId, body.Stores![0].StoreId);
    }

    [Fact]
    public async Task GetOverview_ReturnsPerPrinterBreakdown()
    {
        const int storeId = 985;
        const int printerId = 5985;
        var now = DateTimeOffset.UtcNow;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

            if (!await db.Stores.AnyAsync(s => s.StoreId == storeId))
            {
                db.Stores.Add(new Store { StoreId = storeId, Name = "Printer Breakdown Store", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            if (!await db.Printers.AnyAsync(p => p.PrinterId == printerId))
            {
                db.Printers.Add(new Printer { PrinterId = printerId, StoreId = storeId, PrinterName = "Breakdown Printer", SpoolQueue = @"\\host\q985", Host = "host", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == storeId).ToListAsync());
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: printerId));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Routed, storeId: storeId, printerId: printerId));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.ErrorFinal, storeId: storeId, printerId: printerId, attemptCount: 2));
            AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Pending, storeId: storeId, printerId: null));
            await db.SaveChangesAsync();
        }

        var response = await Client.GetAsync($"/api/dashboard/overview?window=today&storeId={storeId}");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<DashboardOverviewResponse>(response);
        var store = Assert.Single(body!.Stores!);
        Assert.Equal(3, store.QueuedCurrent);           // 2 en printer 5985 (Pending+Routed) + 1 sin asignar
        Assert.Equal(1, store.UnassignedQueueCurrent);
        var printer = Assert.Single(store.Printers!);
        Assert.Equal(printerId, printer.PrinterId);
        Assert.Equal(2, printer.QueueCurrent);   // Pending + Routed
        Assert.Equal(1, printer.FailedWindow);   // ErrorFinal
        Assert.Equal(3, printer.ReceivedWindow); // los 3 jobs asignados a esta impresora, creados hoy
    }

    [Fact]
    public void FailedWithoutRetryCurrentPredicate_TranslatesWithHanaProvider()
    {
        var options = new DbContextOptionsBuilder<ImpresorasDbContext>();
        ConfigureHanaProvider(options, "ServerNode=localhost:30015;UID=test;PWD=test;Current Schema=TEST");

        using var db = new ImpresorasDbContext(options.Options);

        var sql = db.PrintJobs
            .Where(DashboardPrintJobPredicates.FailedWithoutRetryCurrent)
            .Select(x => x.JobId)
            .ToQueryString();

        Assert.Contains("printer_print_job", sql, StringComparison.OrdinalIgnoreCase);

        var groupedSql = db.PrintJobs
            .Where(DashboardPrintJobPredicates.FailedWithoutRetryCurrent)
            .GroupBy(x => x.StoreId)
            .Select(x => new { StoreId = x.Key, Count = x.Count() })
            .ToQueryString();

        Assert.Contains("GROUP BY", groupedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetThresholds_ReturnsDefaultsWhenMissing()
    {
        var response = await Client.GetAsync("/api/dashboard/thresholds");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<DashboardThresholdsResponse>(response);
        Assert.NotNull(body);
        Assert.Equal(10, body!.WarningQueueMin);
        Assert.Equal(30, body.CriticalQueueMin);
    }

    [Fact]
    public async Task GetHealth_ReturnsDatabaseCheck()
    {
        var response = await Client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<HealthResponse>(response);
        Assert.NotNull(body);
        Assert.Equal("ok", body!.Status);
        Assert.True(body.Checks?.ContainsKey("database"));
        Assert.Equal("Healthy", body.Checks!["database"]);
    }

    private async Task SeedDashboardScenarioAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        var now = DateTimeOffset.UtcNow;

        if (!await db.Stores.AnyAsync(s => s.StoreId == DashboardTestStoreId))
        {
            db.Stores.Add(new Store
            {
                StoreId = DashboardTestStoreId,
                Name = "Dashboard Test Store",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        if (!await db.Printers.AnyAsync(p => p.PrinterId == 5880))
        {
            db.Printers.Add(new Printer
            {
                PrinterId = 5880,
                StoreId = DashboardTestStoreId,
                PrinterName = "Dashboard Test Printer",
                SpoolQueue = @"\\host\queue",
                Host = "host",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == DashboardTestStoreId).ToListAsync());

        AddJobWithEvent(db, MakeJob(now, PrintJobStatus.Pending, attemptCount: 0));
        AddJobWithEvent(db, MakeJob(now, PrintJobStatus.PrintedConfirmed, attemptCount: 1));
        AddJobWithEvent(db, MakeJob(now, PrintJobStatus.ErrorFinal, attemptCount: 2));

        await db.SaveChangesAsync();
    }

    private async Task SeedSecondStoreWithJobAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        var now = DateTimeOffset.UtcNow;
        const int otherStoreId = 881;

        if (!await db.Stores.AnyAsync(s => s.StoreId == otherStoreId))
        {
            db.Stores.Add(new Store
            {
                StoreId = otherStoreId,
                Name = "Other Dashboard Store",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        db.PrintJobs.RemoveRange(await db.PrintJobs.Where(j => j.StoreId == otherStoreId).ToListAsync());
        db.PrintJobs.Add(MakeJob(now, PrintJobStatus.Pending, storeId: otherStoreId, attemptCount: 0));
        await db.SaveChangesAsync();
    }

    private const int UnspecifiedPrinterId = -1;

    /// <summary>
    /// "printed"/"failed" ahora se calculan desde PrintJobEvents (KPI-P1-001/002), no solo desde
    /// el Status/UpdatedAtUtc actual del job — sembrar el evento correspondiente junto al job.
    /// </summary>
    private static void AddJobWithEvent(ImpresorasDbContext db, PrintJob job)
    {
        db.PrintJobs.Add(job);

        var isPrintedOrFailedStatus = job.Status is PrintJobStatus.SpoolAccepted or PrintJobStatus.PrintedConfirmed
            or PrintJobStatus.PrintedUnknown or PrintJobStatus.ErrorFinal or PrintJobStatus.RetryScheduled;
        if (isPrintedOrFailedStatus)
        {
            db.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = job.JobId,
                EventType = "StatusChanged",
                NewStatus = job.Status,
                ActorType = "system",
                OccurredAtUtc = job.UpdatedAtUtc
            });
        }
    }

    private static PrintJob MakeJob(
        DateTimeOffset now,
        PrintJobStatus status,
        int attemptCount = 0,
        int storeId = DashboardTestStoreId,
        int? printerId = UnspecifiedPrinterId)
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
            PrinterId = printerId != UnspecifiedPrinterId
                ? printerId
                : (status is PrintJobStatus.Pending or PrintJobStatus.Routed ? 5880 : null),
            AttemptCount = attemptCount,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    private static void ConfigureHanaProvider(DbContextOptionsBuilder options, string connectionString)
    {
        var hanaAssembly = Assembly.Load("Sap.EntityFrameworkCore.Hana.v8.0");
        var extensionsType = hanaAssembly.GetType("Microsoft.EntityFrameworkCore.HanaDbContextOptionsBuilderExtensions")
                             ?? hanaAssembly.GetType("Sap.EntityFrameworkCore.Hana.Infrastructure.HanaDbContextOptionsBuilderExtensions")
                             ?? hanaAssembly.GetTypes().FirstOrDefault(t => t.Name == "HanaDbContextOptionsBuilderExtensions");

        var useHana = extensionsType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name is "UseHana" or "AddHana")
            .Where(m =>
            {
                var parameters = m.GetParameters();
                return parameters.Length >= 1
                       && typeof(DbContextOptionsBuilder).IsAssignableFrom(parameters[0].ParameterType);
            })
            .OrderByDescending(m => m.GetParameters().Any(p => p.ParameterType == typeof(string)))
            .ThenByDescending(m => m.GetParameters().Length)
            .FirstOrDefault();

        Assert.NotNull(useHana);

        var args = useHana.GetParameters()
            .Select((p, index) =>
            {
                if (index == 0)
                    return (object?)options;
                if (p.ParameterType == typeof(string))
                    return connectionString;
                if (p.HasDefaultValue)
                    return p.DefaultValue;
                return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
            })
            .ToArray();

        useHana.Invoke(null, args);
    }

    private sealed class DashboardOverviewResponse
    {
        public DashboardKpis? Kpis { get; set; }
        public List<StoreRow>? Stores { get; set; }
    }

    private sealed class DashboardKpis
    {
        public int Received { get; set; }
        public int Printed { get; set; }
        public int Failed { get; set; }
        public int QueueCurrent { get; set; }
        public int FailedWithoutRetryCurrent { get; set; }
        public int ActivePrinters { get; set; }
        public int ActiveStores { get; set; }
    }

    private sealed class StoreRow
    {
        public int StoreId { get; set; }
        public string? Health { get; set; }
        public int QueuedCurrent { get; set; }
        public int UnassignedQueueCurrent { get; set; }
        public List<PrinterRow>? Printers { get; set; }
    }

    private sealed class PrinterRow
    {
        public int PrinterId { get; set; }
        public int QueueCurrent { get; set; }
        public int FailedWindow { get; set; }
        public int ReceivedWindow { get; set; }
    }

    private sealed class DashboardThresholdsResponse
    {
        public int WarningQueueMin { get; set; }
        public int CriticalQueueMin { get; set; }
    }

    private sealed class HealthResponse
    {
        public string? Status { get; set; }
        public Dictionary<string, string>? Checks { get; set; }
    }
}
