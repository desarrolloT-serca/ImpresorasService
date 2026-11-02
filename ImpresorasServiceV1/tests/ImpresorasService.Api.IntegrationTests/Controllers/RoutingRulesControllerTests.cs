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
        // Una impresora por tienda: una regla de la tienda 2 no puede apuntar a una impresora de la 1.
        var printerStore1 = await CreatePrinterAsync(storeId: 1);
        var printerStore2 = await CreatePrinterAsync(storeId: 2);
        await CreateRuleAsync(printerStore1.PrinterId, storeId: 1);
        await CreateRuleAsync(printerStore2.PrinterId, storeId: 2);

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
            documentType = Tipo(),
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

    [Fact]
    public async Task Create_ConFinAntesDelInicio_Returns400()
    {
        var printer = await CreatePrinterAsync();
        var desde = DateTimeOffset.UtcNow;
        var request = new
        {
            priority = 10, storeId = (int?)1, documentType = Tipo(), printerId = printer.PrinterId,
            isActive = true, validFromUtc = desde, validToUtc = desde.AddHours(-1), createdBy = "test"
        };

        var response = await Client.PostAsJsonAsync("/api/routingrules", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ConPrioridadNegativa_Returns400()
    {
        var printer = await CreatePrinterAsync();
        var request = new { priority = -1, storeId = (int?)1, documentType = Tipo(), printerId = printer.PrinterId, isActive = true, createdBy = "test" };

        var response = await Client.PostAsJsonAsync("/api/routingrules", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Con la misma tienda, tipo, canal y prioridad, una de las dos no se usaría nunca: se rechaza
    /// aunque el tipo venga con otras mayúsculas o espacios. Con otra prioridad es la de respaldo.
    /// </summary>
    [Fact]
    public async Task Create_ReglaRepetida_Returns409_YConOtraPrioridadSePermite()
    {
        var printer = await CreatePrinterAsync();
        var tipo = Tipo();
        object Regla(int priority, string documentType) => new
        {
            priority, storeId = (int?)1, documentType, channel = "WEB", printerId = printer.PrinterId,
            isActive = true, createdBy = "test"
        };
        (await Client.PostAsJsonAsync("/api/routingrules", Regla(5, tipo))).EnsureSuccessStatusCode();

        var repetida = await Client.PostAsJsonAsync("/api/routingrules", Regla(5, $" {tipo.ToLowerInvariant()} "));
        var respaldo = await Client.PostAsJsonAsync("/api/routingrules", Regla(6, tipo));

        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal(HttpStatusCode.Created, respaldo.StatusCode);
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
            documentType = Tipo(),
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

    /// <summary>
    /// El controlador exige que la impresora de una regla pertenezca a la tienda de esa regla
    /// (RoutingRulesController.Create), así que la tienda debe poder elegirse aquí.
    /// </summary>
    private async Task<PrinterDto> CreatePrinterAsync(int storeId = 1)
    {
        var req = new { printerName = $"P-{Guid.NewGuid():N}"[..12], spoolQueue = $"\\\\srv\\q{Guid.NewGuid():N}"[..20], storeId, isActive = true };
        var res = await Client.PostAsJsonAsync("/api/printers", req);
        res.EnsureSuccessStatusCode();
        var p = await res.Content.ReadFromJsonAsync<PrinterDto>();
        Assert.NotNull(p);
        return p;
    }

    private async Task<RoutingRuleDto> CreateRuleAsync(int printerId, int? storeId = 1)
    {
        var req = new { priority = 10, storeId, documentType = Tipo(), printerId, isActive = true, createdBy = "test" };
        var res = await Client.PostAsJsonAsync("/api/routingrules", req);
        res.EnsureSuccessStatusCode();
        var r = await res.Content.ReadFromJsonAsync<RoutingRuleDto>();
        Assert.NotNull(r);
        return r;
    }

    // Un tipo propio de cada regla: dos activas iguales (tienda, tipo, canal y prioridad) no se admiten
    // y las pruebas comparten base de datos.
    private static string Tipo() => $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private sealed record PrinterDto(int PrinterId, string PrinterName, string SpoolQueue, int StoreId, bool IsActive);
    private sealed record RoutingRuleDto(int RuleId, int Priority, int? StoreId, string? DocumentType, string? Channel, int PrinterId, bool IsActive);

    #endregion
}
