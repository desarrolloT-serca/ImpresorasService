using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// Resolución por prioridad: StoreId+DocumentType+Channel > StoreId+DocumentType > StoreId+Channel > StoreId > Global.
/// Dentro de cada nivel, menor Priority = mayor precedencia.
/// </summary>
public class RoutingResolver : IRoutingResolver
{
    private readonly ImpresorasDbContext _db;
    private readonly TimeProvider _timeProvider;

    // TimeProvider opcional: DI lo inyecta (registrado en AddInfrastructure) y los tests que
    // construyen el resolver a mano siguen compilando sin pasarlo.
    public RoutingResolver(ImpresorasDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<int?> ResolvePrinterAsync(
        int storeId,
        string documentType,
        string channel,
        CancellationToken cancellationToken = default)
    {
        var rules = await LoadActiveRulesAsync(cancellationToken);
        return ResolveFromRules(rules, storeId, documentType, channel);
    }

    public async Task<IReadOnlyList<int?>> ResolveBatchAsync(
        IReadOnlyList<(int storeId, string documentType, string channel)> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0) return Array.Empty<int?>();
        var rules = await LoadActiveRulesAsync(cancellationToken);
        return requests.Select(r => ResolveFromRules(rules, r.storeId, r.documentType, r.channel)).ToArray();
    }

    private async Task<List<RoutingRule>> LoadActiveRulesAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        const bool activeOnly = true;

        var rules = await _db.RoutingRules
            .AsNoTracking()
            .Where(r => r.IsActive == activeOnly)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.RuleId)  // desempate determinista cuando Priority es igual
            .ToListAsync(cancellationToken);

        rules = rules
            .Where(r => r.ValidFromUtc <= now && (r.ValidToUtc == null || r.ValidToUtc >= now))
            .ToList();

        var activePrinterIds = (await _db.Printers
            .AsNoTracking()
            .Where(p => p.IsActive == activeOnly)
            .Select(p => p.PrinterId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        return rules.Where(r => activePrinterIds.Contains(r.PrinterId)).ToList();
    }

    private static int? ResolveFromRules(List<RoutingRule> rules, int storeId, string documentType, string channel)
    {
        var normDoc = NormalizeRequired(documentType);
        var normChannel = NormalizeNullable(channel);

        // Niveles de especificidad (mayor = más específico). Evaluar de mayor a menor.
        return (rules.FirstOrDefault(r => Matches(r, storeId, normDoc, normChannel, specificity: 5))
            ?? rules.FirstOrDefault(r => Matches(r, storeId, normDoc, normChannel, specificity: 4))
            ?? rules.FirstOrDefault(r => Matches(r, storeId, normDoc, normChannel, specificity: 3))
            ?? rules.FirstOrDefault(r => Matches(r, storeId, normDoc, normChannel, specificity: 2))
            ?? rules.FirstOrDefault(r => Matches(r, storeId, normDoc, normChannel, specificity: 1)))
            ?.PrinterId;
    }

    /// <summary>
    /// Comprueba si la regla coincide con el trabajo en el nivel de especificidad dado.
    /// </summary>
    private static bool Matches(RoutingRule r, int storeId, string documentType, string? channel, int specificity)
    {
        var ruleDocumentType = NormalizeNullable(r.DocumentType);
        var ruleChannel = NormalizeNullable(r.Channel);

        return specificity switch
        {
            // StoreId + DocumentType + Channel (coincidencia total)
            5 => r.StoreId == storeId
                && ruleDocumentType == documentType
                && ruleChannel == channel,
            // StoreId + DocumentType (cualquier canal)
            4 => r.StoreId == storeId
                && ruleDocumentType == documentType
                && ruleChannel == null,
            // StoreId + Channel (cualquier tipo de documento)
            3 => r.StoreId == storeId
                && ruleDocumentType == null
                && ruleChannel == channel,
            // StoreId (cualquier tipo de documento y canal)
            2 => r.StoreId == storeId
                && ruleDocumentType == null
                && ruleChannel == null,
            // Global
            1 => r.StoreId == null
                && ruleDocumentType == null
                && ruleChannel == null,
            _ => false
        };
    }

    private static string NormalizeRequired(string value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string? NormalizeNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToUpperInvariant();
    }
}
