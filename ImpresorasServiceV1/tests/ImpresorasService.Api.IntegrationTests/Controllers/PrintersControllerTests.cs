using System.Net;
using System.Net.Http.Json;
using ImpresorasService.Api.IntegrationTests;
using ImpresorasService.Domain.Connectivity;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

/// <summary>
/// Tests de integración para todos los estados posibles del CRUD de impresoras.
/// </summary>
public sealed class PrintersControllerTests : IntegrationTestBase
{
    public PrintersControllerTests(ApiWebApplicationFactory factory) : base(factory)
    {
    }

    private const int ConnectivityTestStoreId = 91;

    #region GET /api/printers

    [Fact]
    public async Task GetAll_WhenEmpty_Returns200AndEmptyArray()
    {
        await Factory.ResetDatabaseAsync();
        var response = await Client.GetAsync("/api/printers");

        response.EnsureSuccessStatusCode();
        var printers = await response.Content.ReadFromJsonAsync<List<PrinterDto>>();
        Assert.NotNull(printers);
        Assert.Empty(printers);
    }

    [Fact]
    public async Task GetAll_WithStoreIdFilter_ReturnsOnlyMatchingPrinters()
    {
        var q1 = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var q2 = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        await CreatePrinterAsync("P1", q1, 1);
        await CreatePrinterAsync("P2", q2, 2);

        var response = await Client.GetAsync("/api/printers?storeId=1");

        response.EnsureSuccessStatusCode();
        var printers = await response.Content.ReadFromJsonAsync<List<PrinterDto>>();
        Assert.NotNull(printers);
        Assert.Single(printers);
        Assert.Equal(1, printers[0].StoreId);
    }

    [Fact]
    public async Task GetAll_WithIsActiveFilter_ReturnsOnlyMatchingPrinters()
    {
        var q1 = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var q2 = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        await CreatePrinterAsync("P1", q1, 1, isActive: true);
        await CreatePrinterAsync("P2", q2, 2, isActive: false);

        var response = await Client.GetAsync("/api/printers?isActive=false");

        response.EnsureSuccessStatusCode();
        var printers = await response.Content.ReadFromJsonAsync<List<PrinterDto>>();
        Assert.NotNull(printers);
        Assert.Single(printers);
        Assert.False(printers[0].IsActive);
    }

    #endregion

    #region GET /api/printers/{id}

    [Fact]
    public async Task GetById_WhenExists_Returns200AndPrinter()
    {
        var q = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var created = await CreatePrinterAsync("P1", q, 1);

        var response = await Client.GetAsync($"/api/printers/{created.PrinterId}");

        response.EnsureSuccessStatusCode();
        var printer = await response.Content.ReadFromJsonAsync<PrinterDto>();
        Assert.NotNull(printer);
        Assert.Equal(created.PrinterId, printer.PrinterId);
        Assert.Equal("P1", printer.PrinterName);
        Assert.Equal(1, printer.StoreId);
    }

    [Fact]
    public async Task GetById_WhenNotExists_Returns404()
    {
        var response = await Client.GetAsync("/api/printers/99999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsCalculatedConnectivityFields()
    {
        await SeedStoreAsync(ConnectivityTestStoreId, isActive: true);
        var uncheckedPrinter = await CreatePrinterAsync("P-Unchecked", $"\\\\srv\\q{Guid.NewGuid():N}"[..30], ConnectivityTestStoreId);
        var noHostPrinter = await CreatePrinterAsync("P-NoHost", $"\\\\srv\\q{Guid.NewGuid():N}"[..30], ConnectivityTestStoreId);
        var okPrinter = await CreatePrinterAsync("P-Ok", $"\\\\srv\\q{Guid.NewGuid():N}"[..30], ConnectivityTestStoreId);
        var errorPrinter = await CreatePrinterAsync("P-Error", $"\\\\srv\\q{Guid.NewGuid():N}"[..30], ConnectivityTestStoreId);
        var inactivePrinter = await CreatePrinterAsync("P-Inactive", $"\\\\srv\\q{Guid.NewGuid():N}"[..30], ConnectivityTestStoreId, isActive: false);

        await UpdatePrinterAsync(noHostPrinter.PrinterId, printer =>
        {
            printer.LastConnectionError = PrinterConnectivityState.HostNotConfiguredError;
        });
        await UpdatePrinterAsync(okPrinter.PrinterId, printer =>
        {
            printer.LastConnectionOk = true;
            printer.LastConnectionCheckAtUtc = DateTimeOffset.UtcNow;
            printer.LastConnectionTransport = "tcp/9100";
            printer.ConnectionFailuresStreak = 0;
        });
        await UpdatePrinterAsync(errorPrinter.PrinterId, printer =>
        {
            printer.LastConnectionOk = false;
            printer.LastConnectionCheckAtUtc = DateTimeOffset.UtcNow;
            printer.LastConnectionError = PrinterConnectivityState.NoConnectionError;
            printer.ConnectionFailuresStreak = 2;
        });

        var response = await Client.GetAsync($"/api/printers?storeId={ConnectivityTestStoreId}");

        response.EnsureSuccessStatusCode();
        var printers = await response.Content.ReadFromJsonAsync<List<PrinterDto>>();
        Assert.NotNull(printers);
        var byId = printers.ToDictionary(x => x.PrinterId);

        Assert.Equal("unchecked", byId[uncheckedPrinter.PrinterId].ConnectivityStatus);
        Assert.Equal("No comprobada", byId[uncheckedPrinter.PrinterId].ConnectivityLabel);
        Assert.Equal("warning", byId[uncheckedPrinter.PrinterId].ConnectivitySeverity);

        Assert.Equal("no_host", byId[noHostPrinter.PrinterId].ConnectivityStatus);
        Assert.Equal("Sin host", byId[noHostPrinter.PrinterId].ConnectivityLabel);
        Assert.Equal("warning", byId[noHostPrinter.PrinterId].ConnectivitySeverity);

        Assert.Equal("ok", byId[okPrinter.PrinterId].ConnectivityStatus);
        Assert.Equal("OK", byId[okPrinter.PrinterId].ConnectivityLabel);
        Assert.Equal("healthy", byId[okPrinter.PrinterId].ConnectivitySeverity);

        Assert.Equal("error", byId[errorPrinter.PrinterId].ConnectivityStatus);
        Assert.Equal("Error", byId[errorPrinter.PrinterId].ConnectivityLabel);
        Assert.Equal("critical", byId[errorPrinter.PrinterId].ConnectivitySeverity);

        Assert.Equal("inactive", byId[inactivePrinter.PrinterId].ConnectivityStatus);
        Assert.Equal("Inactiva", byId[inactivePrinter.PrinterId].ConnectivityLabel);
        Assert.Equal("neutral", byId[inactivePrinter.PrinterId].ConnectivitySeverity);
    }

    #endregion

    #region POST /api/printers/{id}/netconnection

    [Fact]
    public async Task NetConnection_WhenPersistFalse_DoesNotUpdateConnectivityTelemetry()
    {
        await SeedStoreAsync(ConnectivityTestStoreId, isActive: true);
        var created = await CreatePrinterAsync("P-NoPersist", $"LocalQueue-{Guid.NewGuid():N}", ConnectivityTestStoreId);

        var response = await Client.PostAsJsonAsync($"/api/printers/{created.PrinterId}/netconnection", new { });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<NetConnectionDto>();
        Assert.NotNull(result);
        Assert.False(result.Reachable);
        Assert.Equal("no_host", result.ConnectivityStatus);

        var persisted = await FindPrinterAsync(created.PrinterId);
        Assert.NotNull(persisted);
        Assert.Null(persisted.LastConnectionCheckAtUtc);
        Assert.Null(persisted.LastConnectionOk);
        Assert.Equal(0, persisted.ConnectionFailuresStreak);
    }

    [Fact]
    public async Task NetConnection_WhenPersistTrueAndHostMissing_StoresNoHostWithoutIncreasingStreak()
    {
        await SeedStoreAsync(ConnectivityTestStoreId, isActive: true);
        var created = await CreatePrinterAsync("P-PersistNoHost", $"LocalQueue-{Guid.NewGuid():N}", ConnectivityTestStoreId);

        var response = await Client.PostAsJsonAsync($"/api/printers/{created.PrinterId}/netconnection?persist=true", new { });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<NetConnectionDto>();
        Assert.NotNull(result);
        Assert.Equal("no_host", result.ConnectivityStatus);
        Assert.Equal(PrinterConnectivityState.HostNotConfiguredError, result.Error);

        var persisted = await FindPrinterAsync(created.PrinterId);
        Assert.NotNull(persisted);
        Assert.False(persisted.LastConnectionOk);
        Assert.NotNull(persisted.LastConnectionCheckAtUtc);
        Assert.Equal(0, persisted.ConnectionFailuresStreak);
        Assert.Equal(PrinterConnectivityState.HostNotConfiguredError, persisted.LastConnectionError);
    }

    [Fact]
    public async Task NetConnection_WhenPersistTrueAndConnectionFails_IncrementsStreakAndStoresCompactError()
    {
        await SeedStoreAsync(ConnectivityTestStoreId, isActive: true);
        var created = await CreatePrinterAsync(
            "P-PersistError",
            $"\\\\srv\\q{Guid.NewGuid():N}"[..30],
            ConnectivityTestStoreId,
            host: "256.256.256.256");
        await UpdatePrinterAsync(created.PrinterId, printer => printer.ConnectionFailuresStreak = 2);

        var response = await Client.PostAsJsonAsync($"/api/printers/{created.PrinterId}/netconnection?persist=true", new { });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<NetConnectionDto>();
        Assert.NotNull(result);
        Assert.Equal("error", result.ConnectivityStatus);
        Assert.Equal(PrinterConnectivityState.NoConnectionError, result.Error);

        var persisted = await FindPrinterAsync(created.PrinterId);
        Assert.NotNull(persisted);
        Assert.False(persisted.LastConnectionOk);
        Assert.NotNull(persisted.LastConnectionCheckAtUtc);
        Assert.Equal(3, persisted.ConnectionFailuresStreak);
        Assert.Equal(PrinterConnectivityState.NoConnectionError, persisted.LastConnectionError);
    }

    [Fact]
    public void ApplyProbeResult_WhenReachable_ResetsConnectivityTelemetry()
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var printer = new Printer
        {
            IsActive = true,
            ConnectionFailuresStreak = 8,
            LastConnectionOk = false,
            LastConnectionError = PrinterConnectivityState.NoConnectionError
        };

        PrinterConnectivityState.ApplyProbeResult(printer, true, "tcp/9100", null, checkedAt);

        Assert.True(printer.LastConnectionOk);
        Assert.Equal(0, printer.ConnectionFailuresStreak);
        Assert.Equal(checkedAt, printer.LastConnectionCheckAtUtc);
        Assert.Equal("tcp/9100", printer.LastConnectionTransport);
        Assert.Null(printer.LastConnectionError);
    }

    #endregion

    #region POST /api/printers

    [Fact]
    public async Task Create_WithValidData_Returns201AndCreatedPrinter()
    {
        var spool = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var request = new { printerName = "P1", spoolQueue = spool, storeId = 1, isActive = true };

        var response = await Client.PostAsJsonAsync("/api/printers", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var printer = await response.Content.ReadFromJsonAsync<PrinterDto>();
        Assert.NotNull(printer);
        Assert.True(printer.PrinterId > 0);
        Assert.Equal("P1", printer.PrinterName);
        Assert.Equal(spool, printer.SpoolQueue);
        Assert.Equal(1, printer.StoreId);
    }

    [Fact]
    public async Task Create_TrimsFieldsAndEmptyCapabilitiesBecomesNull()
    {
        var spool = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var request = new
        {
            printerName = "  P1  ",
            spoolQueue = $"  {spool}  ",
            host = "  srv  ",
            storeId = 1,
            isActive = true,
            capabilitiesJson = "   "
        };

        var response = await Client.PostAsJsonAsync("/api/printers", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var printer = await response.Content.ReadFromJsonAsync<PrinterDto>();
        Assert.NotNull(printer);
        Assert.Equal("P1", printer.PrinterName);
        Assert.Equal(spool, printer.SpoolQueue);
        Assert.Equal("srv", printer.Host);
        Assert.Null(printer.CapabilitiesJson);
    }

    [Fact]
    public async Task Create_WithBlankPrinterName_Returns400()
    {
        var request = new { printerName = "   ", spoolQueue = "\\\\srv\\q1", storeId = 1, isActive = true };

        var response = await Client.PostAsJsonAsync("/api/printers", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithBlankSpoolQueue_Returns400()
    {
        var request = new { printerName = "P1", spoolQueue = "   ", storeId = 1, isActive = true };

        var response = await Client.PostAsJsonAsync("/api/printers", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInvalidCapabilitiesJson_Returns400()
    {
        var request = new
        {
            printerName = "P1",
            spoolQueue = "\\\\srv\\q1",
            storeId = 1,
            isActive = true,
            capabilitiesJson = "{invalid"
        };

        var response = await Client.PostAsJsonAsync("/api/printers", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithDuplicateStoreIdAndSpoolQueue_Returns409()
    {
        var spool = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        await CreatePrinterAsync("P1", spool, 1);
        var request = new { printerName = "P2", spoolQueue = spool, storeId = 1, isActive = true };

        var response = await Client.PostAsJsonAsync("/api/printers", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithUnknownStore_Returns400()
    {
        var spool = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var request = new { printerName = "P1", spoolQueue = spool, storeId = 99999, isActive = true };

        var response = await Client.PostAsJsonAsync("/api/printers", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInactiveStore_Returns400()
    {
        await SeedStoreAsync(97, isActive: false);
        var spool = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var request = new { printerName = "P1", spoolQueue = spool, storeId = 97, isActive = true };

        var response = await Client.PostAsJsonAsync("/api/printers", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region PUT /api/printers/{id}

    [Fact]
    public async Task Update_WhenExists_Returns200AndUpdatedPrinter()
    {
        var q1 = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var q2 = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var created = await CreatePrinterAsync("P1", q1, 1);
        var request = new { printerName = "P1-Updated", spoolQueue = q2, storeId = 1, isActive = true };

        var response = await Client.PutAsJsonAsync($"/api/printers/{created.PrinterId}", request);

        response.EnsureSuccessStatusCode();
        var printer = await response.Content.ReadFromJsonAsync<PrinterDto>();
        Assert.NotNull(printer);
        Assert.Equal("P1-Updated", printer.PrinterName);
        Assert.Equal(q2, printer.SpoolQueue);
    }

    [Fact]
    public async Task Update_WhenNotExists_Returns404()
    {
        var request = new { printerName = "P1", spoolQueue = "\\\\srv\\q1", storeId = 1, isActive = true };

        var response = await Client.PutAsJsonAsync("/api/printers/99999", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithDuplicateStoreIdAndSpoolQueue_Returns409()
    {
        var q1 = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var q2 = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        await CreatePrinterAsync("P1", q1, 1);
        var p2 = await CreatePrinterAsync("P2", q2, 1);
        var request = new { printerName = "P2", spoolQueue = q1, storeId = 1, isActive = true };

        var response = await Client.PutAsJsonAsync($"/api/printers/{p2.PrinterId}", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithUnknownStore_Returns400()
    {
        var q1 = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var q2 = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var created = await CreatePrinterAsync("P1", q1, 1);
        var request = new { printerName = "P1", spoolQueue = q2, storeId = 99999, isActive = true };

        var response = await Client.PutAsJsonAsync($"/api/printers/{created.PrinterId}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithBlankPrinterName_Returns400()
    {
        var q1 = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var created = await CreatePrinterAsync("P1", q1, 1);
        var request = new { printerName = "   ", spoolQueue = q1, storeId = 1, isActive = true };

        var response = await Client.PutAsJsonAsync($"/api/printers/{created.PrinterId}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region DELETE /api/printers/{id}

    [Fact]
    public async Task Delete_WhenExistsAndNoRules_Returns204()
    {
        var spool = $"\\\\srv\\q{Guid.NewGuid():N}"[..30];
        var created = await CreatePrinterAsync("P1", spool, 1);

        var response = await Client.DeleteAsync($"/api/printers/{created.PrinterId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await Client.GetAsync($"/api/printers/{created.PrinterId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_WhenNotExists_Returns404()
    {
        var response = await Client.DeleteAsync("/api/printers/99999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Helpers

    private async Task<PrinterDto> CreatePrinterAsync(
        string name,
        string spoolQueue,
        int storeId,
        bool isActive = true,
        string? host = null)
    {
        var request = new { printerName = name, spoolQueue, host, storeId, isActive };
        var response = await Client.PostAsJsonAsync("/api/printers", request);
        response.EnsureSuccessStatusCode();
        var printer = await response.Content.ReadFromJsonAsync<PrinterDto>();
        Assert.NotNull(printer);
        return printer;
    }

    private async Task UpdatePrinterAsync(int printerId, Action<Printer> update)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        var printer = await db.Printers.FindAsync(printerId);
        Assert.NotNull(printer);
        update(printer);
        await db.SaveChangesAsync();
    }

    private async Task<Printer?> FindPrinterAsync(int printerId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        return await db.Printers.FindAsync(printerId);
    }

    private async Task SeedStoreAsync(int storeId, bool isActive)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
        var store = await db.Stores.FindAsync(storeId);
        if (store is null)
        {
            db.Stores.Add(new Store
            {
                StoreId = storeId,
                Name = $"Store {storeId}",
                IsActive = isActive,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            store.IsActive = isActive;
            store.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    private sealed record PrinterDto(
        int PrinterId,
        string PrinterName,
        string SpoolQueue,
        string? Host,
        int StoreId,
        bool IsActive,
        string? CapabilitiesJson,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        int ConnectionFailuresStreak,
        bool? LastConnectionOk,
        DateTimeOffset? LastConnectionCheckAtUtc,
        string? LastConnectionTransport,
        string? LastConnectionError,
        string ConnectivityStatus,
        string ConnectivityLabel,
        string ConnectivitySeverity,
        string? ConnectivityDetail);

    private sealed record NetConnectionDto(
        bool Reachable,
        int? LatencyMs,
        string? Transport,
        string? Error,
        string? Detail,
        string ConnectivityStatus,
        string ConnectivityLabel,
        string ConnectivitySeverity);

    #endregion
}
