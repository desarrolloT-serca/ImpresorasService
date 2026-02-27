using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// Base para tests de integración. Proporciona helpers HTTP y JSON.
/// </summary>
public abstract class IntegrationTestBase : IClassFixture<ApiWebApplicationFactory>
{
    protected IntegrationTestBase(ApiWebApplicationFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
        Client.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    protected ApiWebApplicationFactory Factory { get; }
    protected HttpClient Client { get; }

    protected static async Task<T?> ReadAsJsonAsync<T>(HttpResponseMessage response, CancellationToken ct = default)
    {
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }

    protected static StringContent JsonContent<T>(T value)
    {
        return new StringContent(
            JsonSerializer.Serialize(value, JsonOptions),
            Encoding.UTF8,
            "application/json");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
