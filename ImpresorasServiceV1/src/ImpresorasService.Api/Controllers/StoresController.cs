using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Authorize(Policy = "EmployeeOrAbove")]
[Route("api/[controller]")]
public class StoresController : ControllerBase
{
    private readonly ImpresorasDbContext _dbContext;

    public StoresController(ImpresorasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? isActive, CancellationToken cancellationToken)
    {
        IQueryable<Store> query = _dbContext.Stores.AsNoTracking();

        var parsedIsActive = ParseNullableBoolean(isActive);
        if (isActive is not null && !parsedIsActive.HasValue)
            return BadRequest(new { error = "El parametro isActive debe ser true/false o 1/0." });
        if (parsedIsActive.HasValue)
            query = query.Where(x => x.IsActive == parsedIsActive.Value);

        var stores = await query
            .OrderBy(x => x.StoreId)
            .Select(x => new
            {
                x.StoreId,
                x.Name,
                x.IsActive,
                UsersCount = _dbContext.Users.Count(u => u.StoreId == x.StoreId),
                PrintersCount = _dbContext.Printers.Count(p => p.StoreId == x.StoreId)
            })
            .ToListAsync(cancellationToken);

        return Ok(stores);
    }

    [HttpGet("{storeId:int}")]
    public async Task<IActionResult> GetById(int storeId, CancellationToken cancellationToken)
    {
        var store = await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .Select(x => new
            {
                x.StoreId,
                x.Name,
                x.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken);

        return store is null ? NotFound() : Ok(store);
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreateStoreRequest request, CancellationToken cancellationToken)
    {
        if (request.StoreId <= 0)
            return BadRequest(new { error = "El numero de tienda debe ser mayor que 0." });
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "El nombre de tienda es obligatorio." });

        var exists = await _dbContext.Stores
            .Where(x => x.StoreId == request.StoreId)
            .Select(x => x.StoreId)
            .Take(1)
            .CountAsync(cancellationToken) > 0;
        if (exists)
            return Conflict(new { error = "Ya existe una tienda con ese numero." });

        var store = new Store
        {
            StoreId = request.StoreId,
            Name = request.Name.Trim(),
            IsActive = request.IsActive,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.Stores.Add(store);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { storeId = store.StoreId }, store);
    }

    [HttpPut("{storeId:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(int storeId, [FromBody] UpdateStoreRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "El nombre de tienda es obligatorio." });

        var store = await _dbContext.Stores.FindAsync(new object[] { storeId }, cancellationToken);
        if (store is null)
            return NotFound();

        store.Name = request.Name.Trim();
        store.IsActive = request.IsActive;
        store.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(store);
    }

    [HttpDelete("{storeId:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(
        int storeId,
        [FromQuery] bool hardDelete = false,
        [FromQuery] bool purgeHistory = false,
        CancellationToken cancellationToken = default)
    {
        var storeExists = await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .Select(x => x.StoreId)
            .Take(1)
            .ToListAsync(cancellationToken);
        if (storeExists.Count == 0)
            return NotFound();

        if (hardDelete)
        {
            var usersAssigned = await _dbContext.Users
                .Where(x => x.StoreId == storeId)
                .Select(x => x.UserId)
                .Take(1)
                .CountAsync(cancellationToken) > 0;
            if (usersAssigned)
            {
                return Conflict(new
                {
                    error = "No se puede eliminar definitivamente: hay usuarios asignados a esta tienda. Reasignalos o eliminalos primero."
                });
            }

            await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var printerIds = await _dbContext.Printers
                .Where(x => x.StoreId == storeId)
                .Select(x => x.PrinterId)
                .ToListAsync(cancellationToken);

            if (printerIds.Count > 0)
            {
                await _dbContext.RoutingRules
                    .Where(x => printerIds.Contains(x.PrinterId))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            await _dbContext.RoutingRules
                .Where(x => x.StoreId == storeId)
                .ExecuteDeleteAsync(cancellationToken);

            if (purgeHistory)
            {
                // Borrado de historico: eventos -> trabajos -> staging de origen.
                var jobIds = await _dbContext.PrintJobs
                    .Where(x => x.StoreId == storeId)
                    .Select(x => x.JobId)
                    .ToListAsync(cancellationToken);

                if (jobIds.Count > 0)
                {
                    await _dbContext.PrintJobEvents
                        .Where(x => jobIds.Contains(x.JobId))
                        .ExecuteDeleteAsync(cancellationToken);
                }

                await _dbContext.PrintJobs
                    .Where(x => x.StoreId == storeId)
                    .ExecuteDeleteAsync(cancellationToken);

                await _dbContext.SourcePrintJobs
                    .Where(x => x.StoreId == storeId)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            await _dbContext.Printers
                .Where(x => x.StoreId == storeId)
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.Stores
                .Where(x => x.StoreId == storeId)
                .ExecuteDeleteAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return Ok(new
            {
                message = purgeHistory
                    ? "Tienda eliminada definitivamente junto con su historico."
                    : "Tienda eliminada definitivamente."
            });
        }

        var nowText = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var affectedPrinters = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"printer_printer\" SET \"is_active\" = FALSE, \"updated_at_utc\" = {nowText} WHERE \"store_id\" = {storeId} AND \"is_active\" = TRUE",
            cancellationToken);
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"printer_store\" SET \"is_active\" = FALSE, \"updated_at_utc\" = {nowText} WHERE \"store_id\" = {storeId}",
            cancellationToken);

        return Ok(new
        {
            message = "Tienda desactivada correctamente.",
            affectedPrinters
        });
    }

    private static bool? ParseNullableBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (normalized == "1") return true;
        if (normalized == "0") return false;
        if (bool.TryParse(normalized, out var boolValue))
            return boolValue;
        return null;
    }
}

public record CreateStoreRequest(int StoreId, string Name, bool IsActive = true);
public record UpdateStoreRequest(string Name, bool IsActive);
