using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
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
        if (request.StoreId.HasValue)
        {
            var storeIsActive = await _dbContext.Stores
                .Where(x => x.StoreId == request.StoreId.Value)
                .Select(x => (bool?)x.IsActive)
                .FirstOrDefaultAsync(cancellationToken);
            if (storeIsActive != true)
                return BadRequest(new { error = "La tienda especificada no existe o esta inactiva." });
        }

        var printerQuery = _dbContext.Printers.Where(x => x.PrinterId == request.PrinterId);
        if (request.StoreId.HasValue)
            printerQuery = printerQuery.Where(x => x.StoreId == request.StoreId.Value);

        if (!await printerQuery.AnyAsync(cancellationToken))
            return BadRequest(new { error = request.StoreId.HasValue
                ? "La impresora no existe o no pertenece a la tienda indicada."
                : "La impresora especificada no existe." });

        var validFrom = request.ValidFromUtc ?? DateTimeOffset.UtcNow;
        if (await ValidarAsync(null, request.Priority, request.StoreId, request.DocumentType, request.Channel,
                request.IsActive, validFrom, request.ValidToUtc, cancellationToken) is (int estado, string error))
            return StatusCode(estado, new { error });

        var rule = new RoutingRule
        {
            Priority = request.Priority,
            StoreId = request.StoreId,
            DocumentType = Limpio(request.DocumentType),
            Channel = Limpio(request.Channel),
            PrinterId = request.PrinterId,
            IsActive = request.IsActive,
            ValidFromUtc = validFrom,
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

        if (request.StoreId.HasValue)
        {
            var storeIsActive = await _dbContext.Stores
                .Where(x => x.StoreId == request.StoreId.Value)
                .Select(x => (bool?)x.IsActive)
                .FirstOrDefaultAsync(cancellationToken);
            if (storeIsActive != true)
                return BadRequest(new { error = "La tienda especificada no existe o esta inactiva." });
        }

        var printerQueryUpdate = _dbContext.Printers.Where(x => x.PrinterId == request.PrinterId);
        if (request.StoreId.HasValue)
            printerQueryUpdate = printerQueryUpdate.Where(x => x.StoreId == request.StoreId.Value);

        if (!await printerQueryUpdate.AnyAsync(cancellationToken))
            return BadRequest(new { error = request.StoreId.HasValue
                ? "La impresora no existe o no pertenece a la tienda indicada."
                : "La impresora especificada no existe." });

        if (await ValidarAsync(id, request.Priority, request.StoreId, request.DocumentType, request.Channel,
                request.IsActive, request.ValidFromUtc, request.ValidToUtc, cancellationToken) is (int estado, string error))
            return StatusCode(estado, new { error });

        rule.Priority = request.Priority;
        rule.StoreId = request.StoreId;
        rule.DocumentType = Limpio(request.DocumentType);
        rule.Channel = Limpio(request.Channel);
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

    private static string? Limpio(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Límites del DDL (sin ellos, un tipo largo daba un 500), fechas coherentes y sin reglas
    // repetidas: con la misma tienda, tipo, canal y prioridad, el resolutor coge siempre la de menor
    // id y la otra no se usaría nunca. Con distinta prioridad sí es legítimo: es la de respaldo si
    // la impresora de la primera está de baja.
    private async Task<(int Estado, string Error)?> ValidarAsync(
        int? id, int priority, int? storeId, string? documentType, string? channel, bool isActive,
        DateTimeOffset validFrom, DateTimeOffset? validTo, CancellationToken cancellationToken)
    {
        if (priority < 0)
            return (StatusCodes.Status400BadRequest, "La prioridad no puede ser negativa.");
        if (Limpio(documentType)?.Length > 80)
            return (StatusCodes.Status400BadRequest, "El tipo de documento no puede superar 80 caracteres.");
        if (Limpio(channel)?.Length > 40)
            return (StatusCodes.Status400BadRequest, "El canal no puede superar 40 caracteres.");
        if (validTo is not null && validTo <= validFrom)
            return (StatusCodes.Status400BadRequest, "La fecha de fin tiene que ser posterior a la de inicio.");
        if (!isActive)
            return null;

        var mismas = await _dbContext.RoutingRules
            .AsNoTracking()
            .Where(r => r.IsActive && r.StoreId == storeId && r.Priority == priority && (id == null || r.RuleId != id))
            .Select(r => new { r.RuleId, r.DocumentType, r.Channel })
            .ToListAsync(cancellationToken);
        var igual = mismas.FirstOrDefault(r =>
            RoutingResolver.NormalizeNullable(r.DocumentType) == RoutingResolver.NormalizeNullable(documentType)
            && RoutingResolver.NormalizeNullable(r.Channel) == RoutingResolver.NormalizeNullable(channel));
        return igual is null
            ? null
            : (StatusCodes.Status409Conflict,
                $"Ya hay una regla igual (#{igual.RuleId}) con la misma prioridad: una de las dos no se usaría nunca.");
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
