namespace ImpresorasService.Application.Services;

public sealed record StoreAlert(
    int StoreId,
    string StoreName,
    string Health,
    string HealthReason,
    int QueuedCurrent,
    int FailedWithoutRetryCurrent);

public sealed record StoreAlertInput(
    int StoreId,
    string StoreName,
    int ConnectedPrinters,
    int QueuedCurrent,
    int FailedWithoutRetryCurrent,
    int MissingHost,
    int ConnMaxStreak,
    int ConnCritical,
    int ConnWarning);

/// <summary>
/// G5.3: puerto exacto de computeHealth()/buildPrioritizedAlerts() de DashboardController.php —
/// una sola fuente de verdad de severidad para overview.stores[].health y overview.alerts,
/// consumida también por StoreHealthAlertBackgroundService (Worker) para Telegram.
/// </summary>
public static class StoreHealthEvaluator
{
    /// <summary>
    /// Un único (health, reason) por tienda — puerto de computeHealth(). Cascada estrictamente
    /// ordenada: severidad "critical" de cualquier métrica antes que "warning" de cualquier otra.
    /// </summary>
    public static (string Health, string Reason) Compute(
        int connectedPrinters,
        int queuedCurrent,
        int failedWithoutRetryCurrent,
        int missingHost,
        int connMaxStreak,
        int connCritical,
        int connWarning,
        ThresholdRuleSet rules)
    {
        var connRule = ThresholdRuleEngine.MatchThresholdRule(rules.Conn, connMaxStreak);
        var queueRule = ThresholdRuleEngine.MatchThresholdRule(rules.Queue, queuedCurrent);
        var failedRule = ThresholdRuleEngine.MatchThresholdRule(rules.Failed, failedWithoutRetryCurrent);
        var missingHostRule = ThresholdRuleEngine.MatchThresholdRule(rules.MissingHost, missingHost);

        if (connCritical > 0)
            return ("critical", "Impresora(s) sin conexion (conectividad)");

        if (connRule is { Severity: "critical" })
            return (ToHealth(connRule.Severity), Prefix(connRule.Severity) + "Impresora(s) sin conexion (conectividad)");

        if (connectedPrinters == 0 && queuedCurrent > 0)
            return ("critical", "Hay cola pero no hay impresoras activas");

        if (failedRule is { Severity: "critical" })
            return (ToHealth(failedRule.Severity), Prefix(failedRule.Severity) + $"Acumula {failedRule.Min} o mas fallos sin reenviar");

        if (queueRule is { Severity: "critical" })
            return (ToHealth(queueRule.Severity), Prefix(queueRule.Severity) + $"Cola actual mayor o igual a {queueRule.Min} trabajos");

        if (connectedPrinters == 0)
            return ("warning", "Sin impresoras activas en la tienda");

        if (connWarning > 0)
            return ("warning", "Impresora(s) pendientes de comprobacion (conectividad)");

        if (connRule is not null)
            return (ToHealth(connRule.Severity), Prefix(connRule.Severity) + "Impresora(s) con fallos de conexion (conectividad)");

        if (missingHostRule is not null && missingHost > 0)
            return (ToHealth(missingHostRule.Severity), Prefix(missingHostRule.Severity) + "Impresora(s) sin host configurado");

        if (failedRule is not null)
            return (ToHealth(failedRule.Severity), Prefix(failedRule.Severity) + "Tiene fallos recientes sin reenviar");

        if (queueRule is not null)
            return (ToHealth(queueRule.Severity), Prefix(queueRule.Severity) + $"Cola actual mayor o igual a {queueRule.Min} trabajos");

        return ("healthy", "Operacion dentro de umbrales");
    }

    /// <summary>
    /// Varios alerts por tienda (hasta 6) — puerto de buildPrioritizedAlerts(). A diferencia de
    /// <see cref="Compute"/>, no hay "primero que matchea gana": cada condición que se cumple
    /// añade su propio alert. Orden final: severidad desc, fallos desc, cola desc, storeId asc.
    /// </summary>
    public static IReadOnlyList<StoreAlert> BuildAlerts(IEnumerable<StoreAlertInput> stores, ThresholdRuleSet rules)
    {
        var alerts = new List<StoreAlert>();

        foreach (var s in stores)
        {
            var connRule = ThresholdRuleEngine.MatchThresholdRule(rules.Conn, s.ConnMaxStreak);
            var failedRule = ThresholdRuleEngine.MatchThresholdRule(rules.Failed, s.FailedWithoutRetryCurrent);
            var queueRule = ThresholdRuleEngine.MatchThresholdRule(rules.Queue, s.QueuedCurrent);
            var missingHostRule = ThresholdRuleEngine.MatchThresholdRule(rules.MissingHost, s.MissingHost);

            if (s.ConnCritical > 0 || s.ConnWarning > 0 || connRule is not null)
            {
                var connHealth = s.ConnCritical > 0
                    ? "critical"
                    : connRule is not null ? ToAlertHealth(connRule.Severity) : "warning";
                alerts.Add(s.ToAlert(connHealth, connHealth == "critical"
                    ? "Impresora(s) sin conexion (conectividad)"
                    : "Impresora(s) pendientes de comprobacion (conectividad)"));
            }

            if (s.ConnectedPrinters == 0 && s.QueuedCurrent > 0)
                alerts.Add(s.ToAlert("critical", "Hay cola pero no hay impresoras activas"));

            if (failedRule is not null)
                alerts.Add(s.ToAlert(ToAlertHealth(failedRule.Severity), $"Acumula {failedRule.Min} o mas fallos sin reenviar"));

            if (queueRule is not null)
                alerts.Add(s.ToAlert(ToAlertHealth(queueRule.Severity), $"Cola actual mayor o igual a {queueRule.Min} trabajos"));

            if (s.ConnectedPrinters == 0)
                alerts.Add(s.ToAlert("warning", "Sin impresoras activas en la tienda"));

            if (missingHostRule is not null && s.MissingHost > 0)
                alerts.Add(s.ToAlert(ToAlertHealth(missingHostRule.Severity), "Impresora(s) sin host configurado"));
        }

        var severityRank = new Dictionary<string, int> { ["critical"] = 3, ["warning"] = 2, ["info"] = 1, ["healthy"] = 0 };
        return alerts
            .OrderByDescending(a => severityRank.GetValueOrDefault(a.Health, 0))
            .ThenByDescending(a => a.FailedWithoutRetryCurrent)
            .ThenByDescending(a => a.QueuedCurrent)
            .ThenBy(a => a.StoreId)
            .ToList();
    }

    /// <summary>ToHealth de computeHealth(): "info" colapsa a "healthy" (a diferencia de BuildAlerts).</summary>
    private static string ToHealth(string severity)
    {
        var s = severity.Trim().ToLowerInvariant();
        return s == "critical" ? "critical" : (s == "warning" ? "warning" : "healthy");
    }

    /// <summary>ToHealth de buildPrioritizedAlerts(): "info" se conserva tal cual (no colapsa).</summary>
    private static string ToAlertHealth(string severity)
    {
        var s = severity.Trim().ToLowerInvariant();
        return s == "critical" ? "critical" : (s == "warning" ? "warning" : "info");
    }

    private static string Prefix(string severity) => severity.Trim().ToLowerInvariant() == "info" ? "Info: " : string.Empty;

    private static StoreAlert ToAlert(this StoreAlertInput s, string health, string reason) =>
        new(s.StoreId, s.StoreName, health, reason, s.QueuedCurrent, s.FailedWithoutRetryCurrent);
}
