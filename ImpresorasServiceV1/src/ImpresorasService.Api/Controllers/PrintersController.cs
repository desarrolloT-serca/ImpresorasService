using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PrintersController : ControllerBase
{
    private readonly ImpresorasDbContext _dbContext;

    public PrintersController(ImpresorasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Lista impresoras, opcionalmente filtradas por tienda.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? storeId,
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        IQueryable<Printer> query = _dbContext.Printers.AsNoTracking();

        if (storeId.HasValue)
            query = query.Where(x => x.StoreId == storeId.Value);
        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        var results = await query
            .OrderBy(x => x.StoreId)
            .ThenBy(x => x.PrinterName)
            .ToListAsync(cancellationToken);

        return Ok(results);
    }

    /// <summary>
    /// Obtiene una impresora por ID.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var printer = await _dbContext.Printers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PrinterId == id, cancellationToken);

        if (printer is null)
            return NotFound();

        return Ok(printer);
    }

    /// <summary>
    /// Crea una impresora. La combinación StoreId + SpoolQueue debe ser única.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePrinterRequest request, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Printers
            .AnyAsync(x => x.StoreId == request.StoreId && x.SpoolQueue == request.SpoolQueue, cancellationToken);

        if (exists)
            return Conflict(new { error = "Ya existe una impresora con el mismo StoreId y SpoolQueue." });

        var printer = new Printer
        {
            PrinterName = request.PrinterName,
            SpoolQueue = request.SpoolQueue,
            StoreId = request.StoreId,
            IsActive = request.IsActive,
            CapabilitiesJson = request.CapabilitiesJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.Printers.Add(printer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = printer.PrinterId }, printer);
    }

    /// <summary>
    /// Actualiza una impresora existente.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePrinterRequest request, CancellationToken cancellationToken)
    {
        var printer = await _dbContext.Printers.FindAsync(new object[] { id }, cancellationToken);

        if (printer is null)
            return NotFound();

        var duplicate = await _dbContext.Printers
            .AnyAsync(x => x.StoreId == request.StoreId && x.SpoolQueue == request.SpoolQueue && x.PrinterId != id, cancellationToken);

        if (duplicate)
            return Conflict(new { error = "Ya existe otra impresora con el mismo StoreId y SpoolQueue." });

        printer.PrinterName = request.PrinterName;
        printer.SpoolQueue = request.SpoolQueue;
        printer.StoreId = request.StoreId;
        printer.IsActive = request.IsActive;
        printer.CapabilitiesJson = request.CapabilitiesJson;
        printer.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(printer);
    }

    /// <summary>
    /// Elimina una impresora. Fallará si hay reglas de enrutado que la referencian.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var printer = await _dbContext.Printers.FindAsync(new object[] { id }, cancellationToken);

        if (printer is null)
            return NotFound();

        _dbContext.Printers.Remove(printer);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("FOREIGN KEY") == true)
        {
            return Conflict(new { error = "No se puede eliminar: existen reglas de enrutado que usan esta impresora." });
        }

        return NoContent();
    }
}

public record CreatePrinterRequest(
    string PrinterName,
    string SpoolQueue,
    int StoreId,
    bool IsActive = true,
    string? CapabilitiesJson = null);

public record UpdatePrinterRequest(
    string PrinterName,
    string SpoolQueue,
    int StoreId,
    bool IsActive,
    string? CapabilitiesJson = null);
