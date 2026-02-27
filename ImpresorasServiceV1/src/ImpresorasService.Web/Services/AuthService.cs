using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ImpresorasService.Web.Services;

/// <summary>
/// Servicio de autenticación (login, logout, usuario actual).
/// </summary>
public class AuthService
{
    private readonly ApiClient _api;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthService(ApiClient api, IHttpContextAccessor httpContextAccessor)
    {
        _api = api;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<LoginResult> LoginAsync(string login, string password, CancellationToken ct = default)
    {
        try
        {
            var response = await _api.PostAsJsonAsync("api/auth/login", new { Login = login, Password = password }, ct);
            if (!response.IsSuccessStatusCode)
                return new LoginResult(false, response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "Credenciales inválidas" : "Error al conectar con la API");

            var user = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
            if (user == null)
                return new LoginResult(false, "Respuesta inválida");

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
                return new LoginResult(false, "Contexto HTTP no disponible");

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(ClaimTypes.Name, user.Login),
                new(ClaimTypes.GivenName, user.DisplayName),
                new(ClaimTypes.Role, user.Role)
            };
            if (user.StoreId.HasValue)
                claims.Add(new Claim("StoreId", user.StoreId.Value.ToString()));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

            return new LoginResult(true);
        }
        catch (HttpRequestException)
        {
            return new LoginResult(false, "No se puede conectar con la API. ¿Está la API en ejecución en el puerto 5105?");
        }
        catch (TaskCanceledException)
        {
            return new LoginResult(false, "Tiempo de espera agotado. Comprueba que la API esté en ejecución.");
        }
        catch (Exception ex)
        {
            return new LoginResult(false, $"Error: {ex.Message}");
        }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public record LoginResult(bool Success, string? Error = null);
}

internal record LoginResponse(int UserId, string Login, string DisplayName, string Role, int? StoreId);
