using ImpresorasService.Application.Services;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// G5.3 (docs/roadmap-integral-2026-07-21.md): puerto del motor de reglas de PHP
/// (computeHealth/buildPrioritizedAlerts, DashboardController.php) a .NET — única fuente de
/// verdad para overview.stores[].health, overview.alerts y las alertas de Telegram del Worker.
/// </summary>
public sealed class StoreHealthEvaluatorTests
{
    private static readonly ThresholdRuleSet Rules = ThresholdRuleSet.Default;

    [Fact]
    public void MatchThresholdRule_ReturnsHighestQualifyingMin()
    {
        var match = ThresholdRuleEngine.MatchThresholdRule(Rules.Queue, value: 12);

        Assert.NotNull(match);
        Assert.Equal(10, match!.Min);
        Assert.Equal("warning", match.Severity);
    }

    [Fact]
    public void MatchThresholdRule_ValueBelowAllMins_ReturnsNull()
    {
        var match = ThresholdRuleEngine.MatchThresholdRule(Rules.Queue, value: 2);

        Assert.Null(match);
    }

    [Fact]
    public void Compute_NoIssues_ReturnsHealthy()
    {
        var (health, reason) = StoreHealthEvaluator.Compute(
            connectedPrinters: 2, queuedCurrent: 0, failedWithoutRetryCurrent: 0, missingHost: 0,
            connMaxStreak: 0, connCritical: 0, connWarning: 0, Rules);

        Assert.Equal("healthy", health);
        Assert.Equal("Operacion dentro de umbrales", reason);
    }

    [Fact]
    public void Compute_ConnCritical_TakesPriorityOverEverythingElse()
    {
        // failedWithoutRetryCurrent=5 también sería critical por su regla, pero conn crítico gana.
        var (health, reason) = StoreHealthEvaluator.Compute(
            connectedPrinters: 2, queuedCurrent: 0, failedWithoutRetryCurrent: 5, missingHost: 0,
            connMaxStreak: 3, connCritical: 1, connWarning: 0, Rules);

        Assert.Equal("critical", health);
        Assert.Equal("Impresora(s) sin conexion (conectividad)", reason);
    }

    [Fact]
    public void Compute_QueueCritical_ReturnsCriticalWithMinInReason()
    {
        var (health, reason) = StoreHealthEvaluator.Compute(
            connectedPrinters: 2, queuedCurrent: 20, failedWithoutRetryCurrent: 0, missingHost: 0,
            connMaxStreak: 0, connCritical: 0, connWarning: 0, Rules);

        Assert.Equal("critical", health);
        Assert.Equal("Cola actual mayor o igual a 20 trabajos", reason);
    }

    /// <summary>
    /// Puerto fiel de una peculiaridad real de PHP (computeHealth, DashboardController.php:756-758):
    /// la severidad "info" del motor de reglas colapsa a health="healthy" (ToHealth no tiene un
    /// tercer estado), pero el texto del motivo conserva el prefijo "Info: " — una tienda puede
    /// aparecer "healthy" con un healthReason que empieza por "Info:". No es un bug del puerto,
    /// es el comportamiento que dashboard.blade.php ya espera (clasifica alertas por substring).
    /// </summary>
    [Fact]
    public void Compute_QueueInfoLevel_IsHealthyButReasonKeepsInfoPrefix()
    {
        var (health, reason) = StoreHealthEvaluator.Compute(
            connectedPrinters: 2, queuedCurrent: 6, failedWithoutRetryCurrent: 0, missingHost: 0,
            connMaxStreak: 0, connCritical: 0, connWarning: 0, Rules);

        Assert.Equal("healthy", health);
        Assert.StartsWith("Info: ", reason);
    }

    [Fact]
    public void Compute_NoConnectedPrintersWithQueue_IsCriticalRegardlessOfRules()
    {
        var (health, reason) = StoreHealthEvaluator.Compute(
            connectedPrinters: 0, queuedCurrent: 3, failedWithoutRetryCurrent: 0, missingHost: 0,
            connMaxStreak: 0, connCritical: 0, connWarning: 0, Rules);

        Assert.Equal("critical", health);
        Assert.Equal("Hay cola pero no hay impresoras activas", reason);
    }

    [Fact]
    public void BuildAlerts_StoreWithMultipleIssues_ProducesOneAlertPerIssue()
    {
        // queue=20 (critical) + failed=5 (critical) + missingHost=1 (info): 3 condiciones
        // independientes, buildPrioritizedAlerts no es "primero que matchea gana" como Compute.
        var input = new StoreAlertInput(
            StoreId: 1, StoreName: "Tienda 1", ConnectedPrinters: 2, QueuedCurrent: 20,
            FailedWithoutRetryCurrent: 5, MissingHost: 1, ConnMaxStreak: 0, ConnCritical: 0, ConnWarning: 0);

        var alerts = StoreHealthEvaluator.BuildAlerts([input], Rules);

        Assert.Equal(3, alerts.Count);
        Assert.Contains(alerts, a => a.HealthReason.Contains("fallos sin reenviar"));
        Assert.Contains(alerts, a => a.HealthReason.Contains("Cola actual"));
        Assert.Contains(alerts, a => a.HealthReason == "Impresora(s) sin host configurado");
    }

    [Fact]
    public void BuildAlerts_SortsBySeverityThenFailedThenQueueThenStoreId()
    {
        var warningStore = new StoreAlertInput(2, "B", 1, 6, 0, 0, 0, 0, 0); // queue=6 -> info
        var criticalStore = new StoreAlertInput(1, "A", 1, 20, 0, 0, 0, 0, 0); // queue=20 -> critical

        var alerts = StoreHealthEvaluator.BuildAlerts([warningStore, criticalStore], Rules);

        Assert.Equal("critical", alerts[0].Health);
        Assert.Equal(1, alerts[0].StoreId);
    }

    [Fact]
    public void Validate_RejectsDescendingThresholds()
    {
        var badRules = new ThresholdRuleSet(
            Queue: [new ThresholdRule(20, "warning"), new ThresholdRule(10, "critical")],
            Failed: Rules.Failed, MissingHost: Rules.MissingHost, Conn: Rules.Conn);

        var errors = ThresholdRuleEngine.Validate(badRules);

        Assert.NotEmpty(errors);
    }
}
