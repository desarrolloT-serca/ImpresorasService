using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// Resolución por prioridad: StoreId+DocumentType+Channel > StoreId+DocumentType > StoreId > Global.
/// Dentro de cada nivel, menor Priority = mayor precedencia.
/// </summary>
public class RoutingResolver : IRoutingResolver
{
    private readonly ImpresorasDbContext _db;

    public RoutingResolver(ImpresorasDbContext db)
    {
        _db = db;
    }

    public async Task<int?> ResolvePrinterAsync(
        int storeId,
        string documentType,
        string channel,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var activeOnly = true;
        var rules = await _db.RoutingRules
            .AsNoTracking()
            .Where(r => r.IsActive == activeOnly)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.RuleId)  // desempate determinista cuando Priority es igual
            .ToListAsync(cancellationToken);

        rules = rules
            .Where(r => r.ValidFromUtc <= now && (r.ValidToUtc == null || r.ValidToUtc >= now))
            .ToList();

        var printerIds = await _db.Printers
            .AsNoTracking()
            .Where(p => p.IsActive == activeOnly)
            .Select(p => p.PrinterId)
            .ToListAsync(cancellationToken);

        var activePrinterIds = printerIds.ToHashSet();
        rules = rules.Where(r => activePrinterIds.Contains(r.PrinterId)).ToList();

        var normalizedDocumentType = NormalizeRequired(documentType);
        var normalizedChannel = NormalizeNullable(channel);

        // Niveles de especificidad (mayor = más específico). Evaluar de mayor a menor.
        var match = rules.FirstOrDefault(r => Matches(r, storeId, normalizedDocumentType, normalizedChannel, specificity: 4))
            ?? rules.FirstOrDefault(r => Matches(r, storeId, normalizedDocumentType, normalizedChannel, specificity: 3))
            ?? rules.FirstOrDefault(r => Matches(r, storeId, normalizedDocumentType, normalizedChannel, specificity: 2))
            ?? rules.FirstOrDefault(r => Matches(r, storeId, normalizedDocumentType, normalizedChannel, specificity: 1));

        return match?.PrinterId;
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
            4 => r.StoreId == storeId
                && ruleDocumentType == documentType
                && ruleChannel == channel,
            // StoreId + DocumentType
            3 => r.StoreId == storeId
                && ruleDocumentType == documentType
                && ruleChannel == null,
            // StoreId
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
