using ImpresorasService.Domain.Entities;

namespace ImpresorasService.Domain.Connectivity;

public static class PrinterConnectivityState
{
    public const string StatusOk = "ok";
    public const string StatusNoHost = "no_host";
    public const string StatusUnchecked = "unchecked";
    public const string StatusError = "error";
    public const string StatusInactive = "inactive";

    public const string SeverityHealthy = "healthy";
    public const string SeverityWarning = "warning";
    public const string SeverityCritical = "critical";
    public const string SeverityNeutral = "neutral";

    public const string HostNotConfiguredError = "HOST_NOT_CONFIGURED";
    public const string NoConnectionError = "NO_CONNECTION";

    public static PrinterConnectivityDescriptor FromPrinter(Printer printer)
        => FromSnapshot(
            printer.IsActive,
            printer.LastConnectionOk,
            printer.LastConnectionCheckAtUtc,
            printer.LastConnectionError,
            printer.ConnectionFailuresStreak);

    public static PrinterConnectivityDescriptor FromSnapshot(
        bool isActive,
        bool? lastConnectionOk,
        DateTimeOffset? lastConnectionCheckAtUtc,
        string? lastConnectionError,
        int connectionFailuresStreak)
    {
        var detail = NormalizeDetail(lastConnectionError);

        if (!isActive)
            return new PrinterConnectivityDescriptor(StatusInactive, "Inactiva", SeverityNeutral, detail);

        if (IsHostNotConfiguredError(detail))
            return new PrinterConnectivityDescriptor(StatusNoHost, "Sin host", SeverityWarning, detail);

        if (!lastConnectionCheckAtUtc.HasValue)
            return new PrinterConnectivityDescriptor(StatusUnchecked, "No comprobada", SeverityWarning, detail);

        if (lastConnectionOk == true)
            return new PrinterConnectivityDescriptor(StatusOk, "OK", SeverityHealthy, detail);

        if (lastConnectionOk == false && connectionFailuresStreak > 0)
            return new PrinterConnectivityDescriptor(StatusError, "Error", SeverityCritical, detail);

        if (lastConnectionOk == false && detail is not null)
            return new PrinterConnectivityDescriptor(StatusError, "Error", SeverityCritical, detail);

        return new PrinterConnectivityDescriptor(StatusUnchecked, "No comprobada", SeverityWarning, detail);
    }

    public static void ApplyProbeResult(
        Printer printer,
        bool reachable,
        string? transport,
        string? error,
        DateTimeOffset checkedAtUtc)
    {
        printer.LastConnectionCheckAtUtc = checkedAtUtc;

        if (reachable)
        {
            printer.LastConnectionOk = true;
            printer.ConnectionFailuresStreak = 0;
            printer.LastConnectionTransport = NormalizeDetail(transport);
            printer.LastConnectionError = null;
            return;
        }

        printer.LastConnectionOk = false;
        printer.LastConnectionTransport = null;

        if (IsHostNotConfiguredError(error))
        {
            printer.ConnectionFailuresStreak = 0;
            printer.LastConnectionError = HostNotConfiguredError;
            return;
        }

        printer.ConnectionFailuresStreak = Math.Clamp(printer.ConnectionFailuresStreak + 1, 0, 999);
        printer.LastConnectionError = NormalizeErrorCode(error);
    }

    public static bool IsHostNotConfiguredError(string? error)
    {
        var normalized = NormalizeDetail(error);
        if (normalized is null)
            return false;

        if (string.Equals(normalized, HostNotConfiguredError, StringComparison.OrdinalIgnoreCase))
            return true;

        var text = normalized.ToLowerInvariant();
        return text.Contains("sin host", StringComparison.Ordinal)
            || text.Contains("host no configurado", StringComparison.Ordinal)
            || text.Contains("host not configured", StringComparison.Ordinal)
            || text.Contains("hostname no configurado", StringComparison.Ordinal);
    }

    private static string NormalizeErrorCode(string? error)
    {
        var normalized = NormalizeDetail(error);
        if (normalized is null)
            return NoConnectionError;

        if (IsHostNotConfiguredError(normalized))
            return HostNotConfiguredError;

        if (string.Equals(normalized, NoConnectionError, StringComparison.OrdinalIgnoreCase))
            return NoConnectionError;

        return NoConnectionError;
    }

    private static string? NormalizeDetail(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

public sealed record PrinterConnectivityDescriptor(
    string ConnectivityStatus,
    string ConnectivityLabel,
    string ConnectivitySeverity,
    string? ConnectivityDetail);
