using System.Net;
using System.Net.Http.Json;
using ImpresorasService.Api.IntegrationTests;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

/// <summary>
/// Tests de integración para todos los estados posibles del CRUD de reglas de enrutado.
/// </summary>
public sealed class RoutingRulesControllerTests : IntegrationTestBase
{
    public RoutingRulesControllerTests(ApiWebApplicationFactory factory) : base(factory)
    {
    }

    #region GET /api/routingrules

    [Fact]
    public async Task GetAll_WhenEmpty_Returns200AndEmptyArray()
    {
        await Factory.ResetDatabaseAsync();
        var response = await Client.GetAsync("/api/routingrules");

        response.EnsureSuccessStatusCode();
        var rules = await response.Content.ReadFromJsonAsync<List<RoutingRuleDto>>();
        Assert.NotNull(rules);
        Assert.Empty(rules);
    }

    [Fact]
    public async Task GetAll_WithFilters_ReturnsFilteredResults()
    {
        var printer = await CreatePrinterAsync();
        await CreateRuleAsync(printer.PrinterId, storeId: 1);
        await CreateRuleAsync(printer.PrinterId, storeId: 2);

        var response = await Client.GetAsync("/api/routingrules?storeId=1");

        response.EnsureSuccessStatusCode();
        var rules = await response.Content.ReadFromJsonAsync<List<RoutingRuleDto>>();
        Assert.NotNull(rules);
        Assert.All(rules, r => Assert.Equal(1, r.StoreId));
    }

    #endregion

    #region GET /api/routingrules/{id}

    [Fact]
    public async Task GetById_WhenExists_Returns200AndRule()
    {
        var printer = await CreatePrinterAsync();
        var rule = await CreateRuleAsync(printer.PrinterId);

        var response = await Client.GetAsync($"/api/routingrules/{rule.RuleId}");

        response.EnsureSuccessStatusCode();
        var r = await response.Content.ReadFromJsonAsync<RoutingRuleDto>();
        Assert.NotNull(r);
        Assert.Equal(rule.RuleId, r.RuleId);
    }

    [Fact]
    public async Task GetById_WhenNotExists_Returns404()
    {
        var response = await Client.GetAsync("/api/routingrules/99999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region POST /api/routingrules

    [Fact]
    public async Task Create_WithValidData_Returns201()
    {
        var printer = await CreatePrinterAsync();
        var request = new
        {
            priority = 10,
            storeId = (int?)1,
            documentType = "FACTURA",
            channel = "DEFAULT",
            printerId = printer.PrinterId,
            isActive = true,
            createdBy = "test"
        };

        var response = await Client.PostAsJsonAsync("/api/routingrules", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var rule = await response.Content.ReadFromJsonAsync<RoutingRuleDto>();
        Assert.NotNull(rule);
        Assert.True(rule.RuleId > 0);
    }

    [Fact]
    public async Task Create_WithNonExistentPrinter_Returns400()
    {
        var request = new
        {
            priority = 10,
            storeId = (int?)1,
            printerId = 99999,
            isActive = true,
            createdBy = "test"
        };

        var response = await Client.PostAsJsonAsync("/api/routingrules", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region PUT /api/routingrules/{id}

    [Fact]
    public async Task Update_WhenExists_Returns200()
    {
        var printer = await CreatePrinterAsync();
        var rule = await CreateRuleAsync(printer.PrinterId);
        var request = new
        {
            priority = 20,
            storeId = (int?)1,
            documentType = "TICKET",
            channel = "DEFAULT",
            printerId = printer.PrinterId,
            isActive = true,
            validFromUtc = DateTimeOffset.UtcNow,
            validToUtc = (DateTimeOffset?)null
        };

        var response = await Client.PutAsJsonAsync($"/api/routingrules/{rule.RuleId}", request);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Update_WhenNotExists_Returns404()
    {
        var printer = await CreatePrinterAsync();
        var request = new
        {
            priority = 10,
            storeId = (int?)1,
            printerId = printer.PrinterId,
            isActive = true,
            validFromUtc = DateTimeOffset.UtcNow,
            validToUtc = (DateTimeOffset?)null
        };

        var response = await Client.PutAsJsonAsync("/api/routingrules/99999", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region DELETE /api/routingrules/{id}

    [Fact]
    public async Task Delete_WhenExists_Returns204()
    {
        var printer = await CreatePrinterAsync();
        var rule = await CreateRuleAsync(printer.PrinterId);

        var response = await Client.DeleteAsync($"/api/routingrules/{rule.RuleId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WhenNotExists_Returns404()
    {
        var response = await Client.DeleteAsync("/api/routingrules/99999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Helpers

    private async Task<PrinterDto> CreatePrinterAsync()
    {
        var req = new { printerName = "P1", spoolQueue = $"\\\\srv\\q{Guid.NewGuid():N}"[..20], storeId = 1, isActive = true };
        var res = await Client.PostAsJsonAsync("/api/printers", req);
        res.EnsureSuccessStatusCode();
        var p = await res.Content.ReadFromJsonAsync<PrinterDto>();
        Assert.NotNull(p);
        return p;
    }

    private async Task<RoutingRuleDto> CreateRuleAsync(int printerId, int? storeId = 1)
    {
        var req = new { priority = 10, storeId, printerId, isActive = true, createdBy = "test" };
        var res = await Client.PostAsJsonAsync("/api/routingrules", req);
        res.EnsureSuccessStatusCode();
        var r = await res.Content.ReadFromJsonAsync<RoutingRuleDto>();
        Assert.NotNull(r);
        return r;
    }

    private sealed record PrinterDto(int PrinterId, string PrinterName, string SpoolQueue, int StoreId, bool IsActive);
    private sealed record RoutingRuleDto(int RuleId, int Priority, int? StoreId, string? DocumentType, string? Channel, int PrinterId, bool IsActive);

    #endregion
}
