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

        var rules = await _db.RoutingRules
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Priority)
            .ToListAsync(cancellationToken);

        rules = rules
            .Where(r => r.ValidFromUtc <= now && (r.ValidToUtc == null || r.ValidToUtc >= now))
            .ToList();

        var printerIds = await _db.Printers
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.PrinterId)
            .ToListAsync(cancellationToken);

        var activePrinterIds = printerIds.ToHashSet();
        rules = rules.Where(r => activePrinterIds.Contains(r.PrinterId)).ToList();

        // Niveles de especificidad (mayor = más específico). Evaluar de mayor a menor.
        var match = rules.FirstOrDefault(r => Matches(r, storeId, documentType, channel, specificity: 4))
            ?? rules.FirstOrDefault(r => Matches(r, storeId, documentType, channel, specificity: 3))
            ?? rules.FirstOrDefault(r => Matches(r, storeId, documentType, channel, specificity: 2))
            ?? rules.FirstOrDefault(r => Matches(r, storeId, documentType, channel, specificity: 1));

        return match?.PrinterId;
    }

    /// <summary>
    /// Comprueba si la regla coincide con el trabajo en el nivel de especificidad dado.
    /// </summary>
    private static bool Matches(RoutingRule r, int storeId, string documentType, string channel, int specificity)
    {
        return specificity switch
        {
            // StoreId + DocumentType + Channel (coincidencia total)
            4 => r.StoreId == storeId
                && r.DocumentType == documentType
                && r.Channel == channel,
            // StoreId + DocumentType
            3 => r.StoreId == storeId
                && r.DocumentType == documentType
                && r.Channel == null,
            // StoreId
            2 => r.StoreId == storeId
                && r.DocumentType == null
                && r.Channel == null,
            // Global
            1 => r.StoreId == null
                && r.DocumentType == null
                && r.Channel == null,
            _ => false
        };
    }
}
