using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ImpresorasDbContext _db;

    public AuthController(ImpresorasDbContext db)
    {
        _db = db;
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
        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Login y contraseña son requeridos" });

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Login == request.Login, cancellationToken);

        if (user == null)
            return Unauthorized(new { error = "Credenciales inválidas" });

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { error = "Credenciales inválidas" });

        return Ok(new LoginResponse(
            user.UserId,
            user.Login,
            user.DisplayName ?? user.Login,
            user.Role,
            user.StoreId));
    }
}

public record LoginRequest(string Login, string Password);

public record LoginResponse(int UserId, string Login, string DisplayName, string Role, int? StoreId);
