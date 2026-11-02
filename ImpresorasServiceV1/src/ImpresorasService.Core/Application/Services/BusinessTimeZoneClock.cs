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
    /// constructor del controller — A-KPI-07. Cae a UTC.
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

    /// <summary>Las 00:00 de ese día en la zona de negocio (filtro de fechas de la cola).</summary>
    public static DateTimeOffset DayStartUtc(DateOnly day, TimeZoneInfo businessTimeZone)
    {
        var date = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(date, businessTimeZone.GetUtcOffset(date));
    }

    /// <summary>
    /// Inicio del periodo del dashboard («today», «7d», «30d»; otro valor = «today»). El mismo para sus
    /// KPI y para los registros que se abren desde ellos, como el de reimpresiones (auditoría L8).
    /// </summary>
    public static DateTimeOffset WindowStartUtc(string? window, DateTimeOffset nowUtc, TimeZoneInfo businessTimeZone) =>
        NormalizeWindow(window) switch
        {
            "7d" => nowUtc.AddDays(-7),
            "30d" => nowUtc.AddDays(-30),
            // "today" en la timezone de negocio (Dashboard:BusinessTimeZone), no en la del servidor.
            _ => TodayStartUtc(nowUtc, businessTimeZone)
        };

    public static string NormalizeWindow(string? window)
    {
        var normalized = (window ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "7d" or "30d" ? normalized : "today";
    }
}
