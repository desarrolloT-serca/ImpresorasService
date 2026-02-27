using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RoutingRulesController : ControllerBase
{
    private readonly ImpresorasDbContext _dbContext;

    public RoutingRulesController(ImpresorasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Lista reglas de enrutado con filtros opcionales.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? storeId,
        [FromQuery] bool? isActive,
        [FromQuery] int? printerId,
        CancellationToken cancellationToken)
    {
        IQueryable<RoutingRule> query = _dbContext.RoutingRules
            .AsNoTracking()
            .Include(x => x.Printer);

        if (storeId.HasValue)
            query = query.Where(x => x.StoreId == storeId.Value);
        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);
        if (printerId.HasValue)
            query = query.Where(x => x.PrinterId == printerId.Value);

        var results = await query
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.StoreId)
            .ThenBy(x => x.DocumentType)
            .ToListAsync(cancellationToken);

        return Ok(results);
    }

    /// <summary>
    /// Obtiene una regla por ID.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var rule = await _dbContext.RoutingRules
            .AsNoTracking()
            .Include(x => x.Printer)
            .FirstOrDefaultAsync(x => x.RuleId == id, cancellationToken);

        if (rule is null)
            return NotFound();

        return Ok(rule);
    }

    /// <summary>
    /// Crea una regla de enrutado. La impresora debe existir.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoutingRuleRequest request, CancellationToken cancellationToken)
    {
        var printerExists = await _dbContext.Printers
            .AnyAsync(x => x.PrinterId == request.PrinterId, cancellationToken);

        if (!printerExists)
            return BadRequest(new { error = "La impresora especificada no existe." });

        var rule = new RoutingRule
        {
            Priority = request.Priority,
            StoreId = request.StoreId,
            DocumentType = request.DocumentType,
            Channel = request.Channel,
            PrinterId = request.PrinterId,
            IsActive = request.IsActive,
            ValidFromUtc = request.ValidFromUtc ?? DateTimeOffset.UtcNow,
            ValidToUtc = request.ValidToUtc,
            CreatedBy = request.CreatedBy,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.RoutingRules.Add(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = rule.RuleId }, rule);
    }

    /// <summary>
    /// Actualiza una regla existente.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateRoutingRuleRequest request, CancellationToken cancellationToken)
    {
        var rule = await _dbContext.RoutingRules.FindAsync(new object[] { id }, cancellationToken);

        if (rule is null)
            return NotFound();

        var printerExists = await _dbContext.Printers
            .AnyAsync(x => x.PrinterId == request.PrinterId, cancellationToken);

        if (!printerExists)
            return BadRequest(new { error = "La impresora especificada no existe." });

        rule.Priority = request.Priority;
        rule.StoreId = request.StoreId;
        rule.DocumentType = request.DocumentType;
        rule.Channel = request.Channel;
        rule.PrinterId = request.PrinterId;
        rule.IsActive = request.IsActive;
        rule.ValidFromUtc = request.ValidFromUtc;
        rule.ValidToUtc = request.ValidToUtc;
        rule.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(rule);
    }

    /// <summary>
    /// Elimina una regla de enrutado.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var rule = await _dbContext.RoutingRules.FindAsync(new object[] { id }, cancellationToken);

        if (rule is null)
            return NotFound();

        _dbContext.RoutingRules.Remove(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}

public record CreateRoutingRuleRequest(
    int Priority,
    int? StoreId,
    string? DocumentType,
    string? Channel,
    int PrinterId,
    bool IsActive = true,
    DateTimeOffset? ValidFromUtc = null,
    DateTimeOffset? ValidToUtc = null,
    string CreatedBy = "system");

public record UpdateRoutingRuleRequest(
    int Priority,
    int? StoreId,
    string? DocumentType,
    string? Channel,
    int PrinterId,
    bool IsActive,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc);
