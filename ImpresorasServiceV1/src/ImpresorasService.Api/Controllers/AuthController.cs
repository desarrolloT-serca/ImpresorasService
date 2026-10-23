using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ImpresorasService.Api.Security;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// Sesión del frontend React (src/ImpresorasService.Web): el mismo JWT que <c>token</c>, pero en
    /// una cookie que el navegador no deja leer, como la sesión del ERP. Program.cs la lee si la
    /// petición no trae cabecera Authorization (la que usa el PHP).
    /// </summary>
    public const string SessionCookie = "impresoras_sesion";

    private readonly ImpresorasDbContext _db;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public AuthController(ImpresorasDbContext db, IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
        _config = config;
        _env = env;
    }

    /// <summary>
    /// Login con usuario y contraseña. Valida contra la tabla Users, abre la sesión del frontend
    /// React (cookie) y devuelve los datos del usuario, sin el token.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var (user, error) = await ValidateCredentialsAsync(request.Login, request.Password, cancellationToken);
        if (user == null)
            return Unauthorized(new { error = error ?? "Credenciales inválidas" });

        Response.Cookies.Append(SessionCookie, GenerateJwt(user), SessionCookieOptions(DateTimeOffset.UtcNow.AddHours(8)));
        return Ok(ToLoginResponse(user));
    }

    /// <summary>
    /// Usuario de la sesión. El frontend lo pide al cargar y cada minuto: sin límite de intentos
    /// (detrás de Nginx el límite es global y lo agotarían entre todas las tiendas).
    /// </summary>
    [HttpGet("me")]
    [Authorize(Policy = "EmployeeOrAbove")]
    [DisableRateLimiting]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var user = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken)
            : null;
        return user == null ? Unauthorized() : Ok(ToLoginResponse(user));
    }

    /// <summary>
    /// Cierra la sesión del frontend React borrando su cookie. No sube <c>TokenVersion</c>: las
    /// cuentas son de tienda y eso echaría también a los compañeros.
    /// </summary>
    [HttpPost("logout")]
    [DisableRateLimiting]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(SessionCookie, SessionCookieOptions(null));
        return NoContent();
    }

    /// <summary>
    /// Login que devuelve JWT para el frontend Laravel.
    /// Token válido 8 horas con claims UserId, Login, Role, StoreId.
    /// </summary>
    [HttpPost("token")]
    public async Task<IActionResult> Token(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var (user, error) = await ValidateCredentialsAsync(request.Login, request.Password, cancellationToken);
        if (user == null)
            return Unauthorized(new { error = error ?? "Credenciales inválidas" });

        var token = GenerateJwt(user);
        var expiresAt = DateTime.UtcNow.AddHours(8);

        return Ok(new TokenResponse(
            token,
            expiresAt,
            new LoginResponse(
                user.UserId,
                user.Login,
                user.DisplayName ?? user.Login,
                RoleCatalog.Normalize(user.Role),
                user.StoreId)));
    }

    private static LoginResponse ToLoginResponse(ImpresorasService.Domain.Entities.User user) => new(
        user.UserId,
        user.Login,
        user.DisplayName ?? user.Login,
        RoleCatalog.Normalize(user.Role),
        user.StoreId);

    private CookieOptions SessionCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        // Strict: otro sitio no puede hacer peticiones con esta sesión (CSRF). Igual que el ERP.
        SameSite = SameSiteMode.Strict,
        // En desarrollo se sirve por http://localhost.
        Secure = !_env.IsDevelopment(),
        Path = "/api",
        Expires = expires,
    };

    private async Task<(ImpresorasService.Domain.Entities.User? user, string? error)> ValidateCredentialsAsync(
        string? login, string? password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            return (null, "Login y contraseña son requeridos");

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Login == login, ct);

        if (user == null)
            return (null, "Credenciales inválidas");

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return (null, "Credenciales inválidas");

        // Después de verificar la contraseña a propósito: quien no acierte las credenciales recibe
        // siempre el mismo mensaje y no puede averiguar qué logins existen.
        if (!user.IsActive)
            return (null, "Usuario desactivado. Contacte con un administrador.");

        return (user, null);
    }

    private string GenerateJwt(ImpresorasService.Domain.Entities.User user)
    {
        var secret = _config["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Jwt:Secret no configurado.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var normalizedRole = RoleCatalog.Normalize(user.Role);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.Login),
            new(ClaimTypes.Role, normalizedRole)
        };
        if (user.StoreId.HasValue)
            claims.Add(new Claim("StoreId", user.StoreId.Value.ToString()));

        // Version con la que se emite. UserRevocationValidator la compara contra la de la base de
        // datos en cada petición: si la contraseña cambió, este token deja de valer.
        claims.Add(new Claim(UserRevocationValidator.TokenVersionClaim, user.TokenVersion.ToString()));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "ImpresorasService",
            audience: _config["Jwt:Audience"] ?? "ImpresorasService",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record LoginRequest(string Login, string Password);

public record LoginResponse(int UserId, string Login, string DisplayName, string Role, int? StoreId);

public record TokenResponse(string Token, DateTime ExpiresAt, LoginResponse User);
