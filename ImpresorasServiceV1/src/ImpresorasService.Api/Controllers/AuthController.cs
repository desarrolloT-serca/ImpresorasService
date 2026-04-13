using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ImpresorasService.Api.Security;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ImpresorasDbContext _db;
    private readonly IConfiguration _config;

    public AuthController(ImpresorasDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    /// <summary>
    /// Login con usuario y contraseña. Valida contra la tabla Users.
    /// Devuelve datos del usuario si las credenciales son correctas.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var (user, error) = await ValidateCredentialsAsync(request.Login, request.Password, cancellationToken);
        if (user == null)
            return Unauthorized(new { error = error ?? "Credenciales inválidas" });

        return Ok(new LoginResponse(
            user.UserId,
            user.Login,
            user.DisplayName ?? user.Login,
            RoleCatalog.Normalize(user.Role),
            user.StoreId));
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

        return (user, null);
    }

    private string GenerateJwt(ImpresorasService.Domain.Entities.User user)
    {
        var secret = _config["Jwt:Secret"] ?? "ImpresorasService-V1-SecretKey-Min32Chars!!";
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
