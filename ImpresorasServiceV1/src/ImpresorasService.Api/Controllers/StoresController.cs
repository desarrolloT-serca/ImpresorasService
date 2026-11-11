using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Controllers;

/// <summary>
/// Tiendas: los almacenes del ERP (vista printer_store sobre gesserca.fdalmacen). Solo lectura: el alta,
/// los cambios y la baja se hacen en el ERP (11/11/2026, ERP_SERCA docs/72).
/// </summary>
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
                x.IsActive
            })
            .ToListAsync(cancellationToken);

        var printerCounts = await _dbContext.Printers
            .AsNoTracking()
            .GroupBy(x => x.StoreId)
            .Select(x => new { StoreId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Count, cancellationToken);

        var result = stores.Select(x => new
        {
            x.StoreId,
            x.Name,
            x.IsActive,
            PrintersCount = printerCounts.TryGetValue(x.StoreId, out var printersCount) ? printersCount : 0
        });

        return Ok(result);
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
