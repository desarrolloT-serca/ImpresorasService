using System.Net.Http.Json;

namespace ImpresorasService.Web.Services;

/// <summary>
/// Cliente HTTP para consumir la API de ImpresorasService.
/// Usa IHttpClientFactory con BaseAddress configurado en appsettings (ApiBaseUrl).
/// </summary>
public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(IHttpClientFactory factory)
    {
        _http = factory.CreateClient("Api");
    }

    public HttpClient Http => _http;

    public Task<T?> GetFromJsonAsync<T>(string requestUri, CancellationToken ct = default) =>
        _http.GetFromJsonAsync<T>(requestUri, ct);

    public Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken ct = default) =>
        _http.GetAsync(requestUri, ct);

    public Task<HttpResponseMessage> PostAsJsonAsync<T>(string requestUri, T value, CancellationToken ct = default) =>
        _http.PostAsJsonAsync(requestUri, value, ct);

    public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent? content, CancellationToken ct = default) =>
        _http.PostAsync(requestUri, content, ct);

    public Task<HttpResponseMessage> PutAsJsonAsync<T>(string requestUri, T value, CancellationToken ct = default) =>
        _http.PutAsJsonAsync(requestUri, value, ct);

    public Task<HttpResponseMessage> DeleteAsync(string requestUri, CancellationToken ct = default) =>
        _http.DeleteAsync(requestUri, ct);
}
