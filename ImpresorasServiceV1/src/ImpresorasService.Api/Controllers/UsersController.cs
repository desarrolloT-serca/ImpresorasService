using ImpresorasService.Api.Security;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly ImpresorasDbContext _dbContext;

    public UsersController(ImpresorasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users
            .AsNoTracking()
            .OrderBy(x => x.Login)
            .Select(x => new
            {
                x.UserId,
                x.Login,
                x.DisplayName,
                Role = RoleCatalog.Normalize(x.Role),
                x.StoreId
            })
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.UserId == id)
            .Select(x => new
            {
                x.UserId,
                x.Login,
                x.DisplayName,
                Role = RoleCatalog.Normalize(x.Role),
                x.StoreId
            })
            .FirstOrDefaultAsync(cancellationToken);

        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        var validationError = await ValidateRequestAsync(
            request.Login,
            request.Password,
            request.Role,
            request.StoreId,
            cancellationToken);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        var exists = (await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.Login == request.Login.Trim())
            .Select(x => x.UserId)
            .Take(1)
            .ToListAsync(cancellationToken))
            .Count > 0;
        if (exists)
            return Conflict(new { error = "Ya existe un usuario con ese login." });

        if (!RoleCatalog.TryNormalize(request.Role, out var role))
            return BadRequest(new { error = "El rol no es valido." });

        var user = new User
        {
            Login = request.Login.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Login.Trim() : request.DisplayName.Trim(),
            Role = role,
            StoreId = NeedsStore(role) ? request.StoreId : null,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password.Trim(), BCrypt.Net.BCrypt.GenerateSalt(10))
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = user.UserId }, new
        {
            user.UserId,
            user.Login,
            user.DisplayName,
            user.Role,
            user.StoreId
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FindAsync(new object[] { id }, cancellationToken);
        if (user is null)
            return NotFound();

        var validationError = await ValidateRequestAsync(
            request.Login,
            request.Password ?? "placeholder",
            request.Role,
            request.StoreId,
            cancellationToken,
            id);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        if (!RoleCatalog.TryNormalize(request.Role, out var role))
            return BadRequest(new { error = "El rol no es valido." });

        user.Login = request.Login.Trim();
        user.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Login.Trim() : request.DisplayName.Trim();
        user.Role = role;
        user.StoreId = NeedsStore(role) ? request.StoreId : null;

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password.Trim(), BCrypt.Net.BCrypt.GenerateSalt(10));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new
        {
            user.UserId,
            user.Login,
            user.DisplayName,
            user.Role,
            user.StoreId
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FindAsync(new object[] { id }, cancellationToken);
        if (user is null)
            return NotFound();

        if (IsCurrentUser(user))
            return Conflict(new { error = "No puedes eliminar tu propio usuario." });

        if (RoleCatalog.Normalize(user.Role) == RoleCatalog.Admin)
        {
            var remainingRoles = await _dbContext.Users
                .AsNoTracking()
                .Where(x => x.UserId != id)
                .Select(x => x.Role)
                .ToListAsync(cancellationToken);
            var hasRemainingAdmin = remainingRoles.Any(role => RoleCatalog.Normalize(role) == RoleCatalog.Admin);
            if (!hasRemainingAdmin)
                return Conflict(new { error = "No se puede eliminar el ultimo administrador." });
        }

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<string?> ValidateRequestAsync(
        string? login,
        string? password,
        string? role,
        int? storeId,
        CancellationToken ct,
        int? currentUserId = null)
    {
        if (string.IsNullOrWhiteSpace(login))
            return "El login es obligatorio.";
        if (login.Trim().Length < 3)
            return "El login debe tener al menos 3 caracteres.";

        if (!RoleCatalog.TryNormalize(role, out var normalizedRole))
            return "El rol no es valido.";

        if (NeedsStore(normalizedRole) && (!storeId.HasValue || storeId <= 0))
            return "Debe indicar una tienda para el rol seleccionado.";

        if (normalizedRole == RoleCatalog.Admin && storeId.HasValue)
            return "Los administradores no deben tener tienda asignada.";

        if (currentUserId is null && string.IsNullOrWhiteSpace(password))
            return "La contrasena es obligatoria.";
        if (!string.IsNullOrWhiteSpace(password) && password.Trim().Length < 6)
            return "La contrasena debe tener al menos 6 caracteres.";

        var normalizedLogin = login.Trim();
        var loginInUse = (await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.Login == normalizedLogin && (!currentUserId.HasValue || x.UserId != currentUserId.Value))
            .Select(x => x.UserId)
            .Take(1)
            .ToListAsync(ct))
            .Count > 0;
        if (loginInUse)
            return "Ya existe un usuario con ese login.";

        if (NeedsStore(normalizedRole))
        {
            var storeIdValue = storeId.GetValueOrDefault();
            if (storeIdValue <= 0)
                return "La tienda seleccionada no existe o esta inactiva.";

            var storeIsActive = (await _dbContext.Stores
                .AsNoTracking()
                .Where(x => x.StoreId == storeIdValue && x.IsActive)
                .Select(x => x.StoreId)
                .Take(1)
                .ToListAsync(ct))
                .Count > 0;
            if (!storeIsActive)
                return "La tienda seleccionada no existe o esta inactiva.";
        }

        return null;
    }

    private bool IsCurrentUser(User user)
    {
        var claimUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(claimUserId, out var currentUserId) && currentUserId == user.UserId)
            return true;

        var claimLogin = User.FindFirstValue("Login")
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? User.Identity?.Name;

        return !string.IsNullOrWhiteSpace(claimLogin)
            && string.Equals(claimLogin.Trim(), user.Login, StringComparison.OrdinalIgnoreCase);
    }

    private static bool NeedsStore(string normalizedRole)
    {
        return normalizedRole is RoleCatalog.StoreManager or RoleCatalog.Employee;
    }
}

public record CreateUserRequest(
    string Login,
    string Password,
    string Role,
    int? StoreId,
    string? DisplayName = null);

public record UpdateUserRequest(
    string Login,
    string Role,
    int? StoreId,
    string? DisplayName = null,
    string? Password = null);
