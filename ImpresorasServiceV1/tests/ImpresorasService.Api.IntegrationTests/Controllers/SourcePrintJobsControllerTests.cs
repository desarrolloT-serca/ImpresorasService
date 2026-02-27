using System.Net;
using System.Net.Http.Json;
using ImpresorasService.Api.IntegrationTests;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

/// <summary>
/// Tests de integración para SourcePrintJobs (origen de pruebas).
/// </summary>
public sealed class SourcePrintJobsControllerTests : IntegrationTestBase
{
    public SourcePrintJobsControllerTests(ApiWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CreateTest_WithValidData_Returns200()
    {
        var request = new
        {
            sourceSystem = "SAP-TEST",
            externalJobId = $"JOB-{Guid.NewGuid():N}",
            storeId = 1,
            documentType = "FACTURA",
            channel = "DEFAULT",
            pdfBlob = new byte[] { 0x25, 0x50, 0x44, 0x46 }
        };

        var response = await Client.PostAsJsonAsync("/api/sourceprintjobs/test", request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreateTestResponseDto>();
        Assert.NotNull(body);
        Assert.True(body.Id > 0);
    }

    [Fact]
    public async Task CreateTest_WithDuplicateSourceExternalId_Returns409()
    {
        await Factory.ResetDatabaseAsync();
        var externalId = $"JOB-{Guid.NewGuid():N}";
        var request = new
        {
            sourceSystem = "SAP-TEST",
            externalJobId = externalId,
            storeId = 1,
            documentType = "FACTURA",
            pdfBlob = new byte[] { 0x25, 0x50, 0x44, 0x46 }
        };

        await Client.PostAsJsonAsync("/api/sourceprintjobs/test", request);
        var response = await Client.PostAsJsonAsync("/api/sourceprintjobs/test", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetPending_Returns200AndArray()
    {
        await Factory.ResetDatabaseAsync();
        var response = await Client.GetAsync("/api/sourceprintjobs");

        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<List<object>>();
        Assert.NotNull(rows);
    }

    #region Helpers

    private sealed record CreateTestResponseDto(long Id, string ExternalJobId, int StoreId);

    #endregion
}
