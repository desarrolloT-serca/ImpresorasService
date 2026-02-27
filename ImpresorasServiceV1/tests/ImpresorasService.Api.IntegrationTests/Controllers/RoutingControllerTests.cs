using System.Net;
using System.Net.Http.Json;
using ImpresorasService.Api.IntegrationTests;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

/// <summary>
/// Tests de integración para el endpoint de resolución de rutas.
/// </summary>
public sealed class RoutingControllerTests : IntegrationTestBase
{
    public RoutingControllerTests(ApiWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Resolve_WhenNoRules_Returns404WithRouteNotFound()
    {
        await Factory.ResetDatabaseAsync();
        var request = new { storeId = 1, documentType = "FACTURA", channel = "DEFAULT" };

        var response = await Client.PostAsJsonAsync("/api/routing/resolve", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await ReadAsJsonAsync<ErrorDto>(response);
        Assert.NotNull(body);
        Assert.Equal("ROUTE_NOT_FOUND", body.Code);
    }

    [Fact]
    public async Task Resolve_WhenRuleExists_Returns200WithPrinter()
    {
        await Factory.ResetDatabaseAsync();
        var printer = await CreatePrinterAsync();
        await CreateGlobalRuleAsync(printer.PrinterId);
        var request = new { storeId = 1, documentType = "FACTURA", channel = "DEFAULT" };

        var response = await Client.PostAsJsonAsync("/api/routing/resolve", request);

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<ResolveResponseDto>(response);
        Assert.NotNull(body);
        Assert.Equal(printer.PrinterId, body.PrinterId);
        Assert.NotNull(body.Printer);
    }

    [Fact]
    public async Task Resolve_RespectsPriorityOrder_StoreIdDocumentTypeChannelFirst()
    {
        await Factory.ResetDatabaseAsync();
        var p1 = await CreatePrinterAsync("P1", 1);
        var p2 = await CreatePrinterAsync("P2", 1);
        await CreateRuleAsync(p1.PrinterId, storeId: 1, documentType: "FACTURA", channel: "DEFAULT", priority: 10);
        await CreateRuleAsync(p2.PrinterId, storeId: 1, documentType: null, channel: null, priority: 5);

        var request = new { storeId = 1, documentType = "FACTURA", channel = "DEFAULT" };
        var response = await Client.PostAsJsonAsync("/api/routing/resolve", request);

        response.EnsureSuccessStatusCode();
        var body = await ReadAsJsonAsync<ResolveResponseDto>(response);
        Assert.NotNull(body);
        Assert.Equal(p1.PrinterId, body.PrinterId);
    }

    #region Helpers

    private async Task<PrinterDto> CreatePrinterAsync(string name = "P1", int storeId = 1)
    {
        var req = new { printerName = name, spoolQueue = $"\\\\srv\\{Guid.NewGuid():N}"[..30], storeId, isActive = true };
        var res = await Client.PostAsJsonAsync("/api/printers", req);
        res.EnsureSuccessStatusCode();
        var p = await res.Content.ReadFromJsonAsync<PrinterDto>();
        Assert.NotNull(p);
        return p;
    }

    private async Task CreateGlobalRuleAsync(int printerId)
    {
        var req = new { priority = 10, storeId = (int?)null, printerId, isActive = true, createdBy = "test" };
        var res = await Client.PostAsJsonAsync("/api/routingrules", req);
        res.EnsureSuccessStatusCode();
    }

    private async Task CreateRuleAsync(int printerId, int? storeId, string? documentType, string? channel, int priority)
    {
        var req = new { priority, storeId, documentType, channel, printerId, isActive = true, createdBy = "test" };
        var res = await Client.PostAsJsonAsync("/api/routingrules", req);
        res.EnsureSuccessStatusCode();
    }

    private sealed record PrinterDto(int PrinterId, string PrinterName, string SpoolQueue, int StoreId);
    private sealed record ResolveResponseDto(int PrinterId, object Printer);
    private sealed record ErrorDto(string Error, string Code);

    #endregion
}
