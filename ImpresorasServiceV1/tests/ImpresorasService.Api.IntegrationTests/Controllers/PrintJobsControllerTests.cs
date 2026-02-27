using System.Net;
using System.Net.Http.Json;
using ImpresorasService.Api.IntegrationTests;
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
    public async Task Route_WhenJobNotExists_Returns400()
    {
        var response = await Client.PostAsync("/api/printjobs/00000000-0000-0000-0000-000000000001/route", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
        var req = new { printerName = "P1", spoolQueue = $"\\\\srv\\{Guid.NewGuid():N}"[..30], storeId = 1, isActive = true };
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
