namespace ImpresorasService.Application.Services;

public sealed record ThresholdRule(int Min, string Severity);

/// <summary>
/// Reglas de severidad de 1 a 3 niveles por métrica (G5.3: puerto del motor de PHP
/// dashboard-threshold-rules.json a .NET, fuente única de verdad).
/// </summary>
public sealed record ThresholdRuleSet(
    IReadOnlyList<ThresholdRule> Queue,
    IReadOnlyList<ThresholdRule> Failed,
    IReadOnlyList<ThresholdRule> MissingHost,
    IReadOnlyList<ThresholdRule> Conn)
{
    public static readonly string[] Severities = { "info", "warning", "critical" };
    public static readonly string[] Metrics = { "queue", "failed", "missingHost", "conn" };

    /// <summary>Semilla inicial (2026-07-21): copia exacta del dashboard-threshold-rules.json de PHP en producción.</summary>
    public static ThresholdRuleSet Default { get; } = new(
        Queue: new[] { new ThresholdRule(5, "info"), new ThresholdRule(10, "warning"), new ThresholdRule(20, "critical") },
        Failed: new[] { new ThresholdRule(1, "warning"), new ThresholdRule(5, "critical") },
        MissingHost: new[] { new ThresholdRule(1, "info") },
        Conn: new[] { new ThresholdRule(1, "info"), new ThresholdRule(2, "warning"), new ThresholdRule(3, "critical") });

    public IReadOnlyList<ThresholdRule> ForMetric(string metric) => metric switch
    {
        "queue" => Queue,
        "failed" => Failed,
        "missingHost" => MissingHost,
        "conn" => Conn,
        _ => Array.Empty<ThresholdRule>()
    };
}
