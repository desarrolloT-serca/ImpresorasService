namespace ImpresorasService.Application.Services;

/// <summary>
/// Normalización, validación y matching de reglas de severidad (G5.3) — puerto exacto de
/// normalizeRuleSet/matchThresholdRule/normalizeThresholdRules/hasAscendingSeverityThresholds
/// de DashboardController.php (PHP), para que Api y Worker compartan el mismo comportamiento.
/// </summary>
public static class ThresholdRuleEngine
{
    /// <summary>Min &gt;= 0, severidad válida (default "warning" si no lo es), orden por (min, severidad).</summary>
    public static IReadOnlyList<ThresholdRule> NormalizeRuleSet(IEnumerable<ThresholdRule> rules)
    {
        var severityOrder = ThresholdRuleSet.Severities;
        return rules
            .Select(r =>
            {
                var min = Math.Max(0, r.Min);
                var severity = (r.Severity ?? "warning").Trim().ToLowerInvariant();
                if (!severityOrder.Contains(severity))
                    severity = "warning";
                return new ThresholdRule(min, severity);
            })
            .OrderBy(r => r.Min)
            .ThenBy(r => Array.IndexOf(severityOrder, r.Severity))
            .ToList();
    }

    /// <summary>La regla de mayor `min` que <paramref name="value"/> todavía cumple (`value &gt;= min`), o null.</summary>
    public static ThresholdRule? MatchThresholdRule(IReadOnlyList<ThresholdRule> rules, int value)
    {
        var normalized = NormalizeRuleSet(rules);
        for (var i = normalized.Count - 1; i >= 0; i--)
        {
            if (value >= normalized[i].Min)
                return normalized[i];
        }

        return null;
    }

    /// <summary>
    /// Valida un ThresholdRuleSet completo: 1-3 reglas por métrica, severidad/min únicos dentro de
    /// cada métrica, y umbrales crecientes con la severidad (Info &lt; Warning &lt; Critica).
    /// Devuelve la lista de errores (vacía si es válido).
    /// </summary>
    public static IReadOnlyList<string> Validate(ThresholdRuleSet rules)
    {
        var errors = new List<string>();
        // Las opcionales (silent, reprints) pueden ir vacías: desactivadas.
        foreach (var metric in ThresholdRuleSet.Metrics.Concat(ThresholdRuleSet.OptionalMetrics))
        {
            var metricRules = rules.ForMetric(metric);
            if (metricRules.Count == 0)
            {
                if (ThresholdRuleSet.Metrics.Contains(metric))
                    errors.Add("Cada grupo debe tener al menos un umbral.");
                continue;
            }
            if (metricRules.Count > ThresholdRuleSet.Severities.Length)
                errors.Add("Cada grupo permite como máximo tres umbrales.");

            var seenSeverity = new HashSet<string>();
            var seenMin = new HashSet<int>();
            foreach (var rule in metricRules)
            {
                var severity = (rule.Severity ?? string.Empty).Trim().ToLowerInvariant();
                if (!ThresholdRuleSet.Severities.Contains(severity))
                {
                    errors.Add("Hay una severidad no válida.");
                    continue;
                }
                if (!seenSeverity.Add(severity))
                    errors.Add("No puede repetirse la misma severidad dentro de un grupo.");
                if (!seenMin.Add(rule.Min))
                    errors.Add("No puede repetirse el mismo valor de umbral dentro de un grupo.");
            }

            if (!HasAscendingSeverityThresholds(metricRules))
                errors.Add("Los umbrales deben crecer con la severidad: Info < Warning < Critica.");
        }

        var h = rules.Horario;
        if (h.Dias.Count == 0 || h.Dias.Any(d => d is < 1 or > 7) || h.Dias.Distinct().Count() != h.Dias.Count)
            errors.Add("El horario de tienda necesita al menos un día de la semana.");
        if (!HorarioTienda.TryHora(h.Desde, out var desde) || !HorarioTienda.TryHora(h.Hasta, out var hasta) || desde >= hasta)
            errors.Add("El horario de tienda va de una hora a otra mayor (HH:mm), en el mismo día.");

        return errors.Distinct().ToList();
    }

    private static bool HasAscendingSeverityThresholds(IReadOnlyList<ThresholdRule> rules)
    {
        var severityOrder = ThresholdRuleSet.Severities;
        var ordered = rules
            .OrderBy(r => Array.IndexOf(severityOrder, (r.Severity ?? string.Empty).Trim().ToLowerInvariant()))
            .ToList();

        int? previousMin = null;
        foreach (var rule in ordered)
        {
            if (previousMin.HasValue && rule.Min <= previousMin.Value)
                return false;

            previousMin = rule.Min;
        }

        return true;
    }
}
