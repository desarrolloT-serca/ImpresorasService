namespace ImpresorasService.Application.Services;

public static class StoreHealthEvaluator
{
    public static (string Health, string Reason) Compute(
        int connectedPrinters,
        int queuedCurrent,
        int failedWithoutRetryCurrent,
        int missingHost,
        int connWarn,
        int connCrit,
        int warningQueueMin,
        int criticalQueueMin,
        int warningFailedMin,
        int criticalFailedMin,
        int missingHostMin,
        int connWarningMin,
        int connCriticalMin,
        string connCritSeverity = "critical",
        string failedCritSeverity = "critical",
        string queueCritSeverity = "critical",
        string connWarnSeverity = "warning",
        string missingHostSeverity = "warning",
        string failedWarnSeverity = "warning",
        string queueWarnSeverity = "warning")
    {
        if (connCrit > 0)
            return (ToHealth(connCritSeverity), "Impresora(s) sin conexion (conectividad)");

        if (connectedPrinters == 0 && queuedCurrent > 0)
            return ("critical", "Hay cola pero no hay impresoras activas");

        if (failedWithoutRetryCurrent >= criticalFailedMin)
            return (ToHealth(failedCritSeverity), $"Acumula {criticalFailedMin} o mas fallos sin reenviar");

        if (queuedCurrent >= criticalQueueMin)
            return (ToHealth(queueCritSeverity), $"Cola actual mayor o igual a {criticalQueueMin} trabajos");

        if (connectedPrinters == 0)
            return ("warning", "Sin impresoras activas en la tienda");

        if (connWarn > 0)
            return (ToHealth(connWarnSeverity), "Impresora(s) con fallos de conexion (conectividad)");

        if (missingHost >= missingHostMin && missingHost > 0)
            return (ToHealth(missingHostSeverity), "Impresora(s) sin host configurado");

        if (failedWithoutRetryCurrent >= warningFailedMin)
            return (ToHealth(failedWarnSeverity), "Tiene fallos recientes sin reenviar");

        if (queuedCurrent >= warningQueueMin)
            return (ToHealth(queueWarnSeverity), $"Cola actual entre {warningQueueMin} y {criticalQueueMin - 1} trabajos");

        return ("healthy", "Operacion dentro de umbrales");
    }

    private static string ToHealth(string severity)
    {
        var s = severity.Trim().ToLowerInvariant();
        return s == "critical" ? "critical" : (s == "warning" ? "warning" : "healthy");
    }
}
