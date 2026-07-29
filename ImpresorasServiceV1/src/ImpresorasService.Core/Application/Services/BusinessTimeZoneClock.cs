using Microsoft.Extensions.Logging;

namespace ImpresorasService.Application.Services;

/// <summary>
/// Resuelve "inicio de hoy" en la timezone de negocio (Dashboard:BusinessTimeZone). Compartido
/// entre la Api (dashboard) y el Worker (alertas de tienda) para que ambos usen el mismo reloj —
/// antes el Worker usaba <c>now.Date</c> (medianoche UTC) mientras la Api ya usaba Europe/Madrid,
/// lo que podía mostrar una tienda saludable en el dashboard y crítica en la alerta por el simple
/// corte horario (KPI-P2-004).
/// </summary>
public static class BusinessTimeZoneClock
{
    /// <summary>
    /// Un id de timezone inválido en config no debe tumbar el dashboard/Worker con un 500 en el
    /// constructor del controller — A-KPI-07 (docs/auditoria-integral-2026-07-21.md). Cae a UTC.
    /// </summary>
    public static TimeZoneInfo Resolve(string? configuredTimeZoneId, ILogger? logger = null, string defaultTimeZoneId = "Europe/Madrid")
    {
        var timeZoneId = string.IsNullOrWhiteSpace(configuredTimeZoneId) ? defaultTimeZoneId : configuredTimeZoneId;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger?.LogWarning(ex, "Dashboard:BusinessTimeZone '{TimeZoneId}' inválido; usando UTC.", timeZoneId);
            return TimeZoneInfo.Utc;
        }
    }

    public static DateTimeOffset TodayStartUtc(DateTimeOffset nowUtc, TimeZoneInfo businessTimeZone)
    {
        var todayDate = TimeZoneInfo.ConvertTime(nowUtc, businessTimeZone).Date;
        return new DateTimeOffset(todayDate, businessTimeZone.GetUtcOffset(todayDate));
    }
}
