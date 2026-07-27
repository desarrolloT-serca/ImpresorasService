using ImpresorasService.Application.Services;

namespace ImpresorasService.Application.Abstractions;

/// <summary>
/// Persistencia de las reglas de severidad de dashboard (G5.3). Fichero JSON, no BD — compartido
/// por Api y Worker vía la misma ruta configurada (Dashboard:ThresholdRulesFilePath).
/// </summary>
public interface IDashboardThresholdRuleStore
{
    Task<ThresholdRuleSet> LoadAsync(CancellationToken ct);

    /// <summary>Valida y persiste. Devuelve la lista de errores (vacía si se guardó correctamente).</summary>
    Task<IReadOnlyList<string>> SaveAsync(ThresholdRuleSet rules, CancellationToken ct);
}
