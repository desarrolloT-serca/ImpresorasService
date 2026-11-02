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

    /// <summary>
    /// Métricas opcionales (27/10/2026): lista vacía = desactivada, así un fichero o un cliente
    /// anterior (PHP) que no las conoce no las activa ni las borra.
    /// </summary>
    public static readonly string[] OptionalMetrics = { "silent", "reprints" };

    /// <summary>Minutos sin recibir ningún trabajo dentro del <see cref="Horario"/> de tienda.</summary>
    public IReadOnlyList<ThresholdRule> Silent { get; init; } = Array.Empty<ThresholdRule>();

    /// <summary>Reimpresiones de hoy (hora de negocio) de la tienda.</summary>
    public IReadOnlyList<ThresholdRule> Reprints { get; init; } = Array.Empty<ThresholdRule>();

    /// <summary>Horario común en el que cuenta <see cref="Silent"/>; fuera de él no avisa.</summary>
    public HorarioTienda Horario { get; init; } = HorarioTienda.Default;

    /// <summary>
    /// Telegram solo avisa dentro de <see cref="Horario"/> (27/10/2026): lo que pase fuera se avisa al
    /// abrir si sigue pasando. Aquí y no en printer_telegram_config para no tocar la BD.
    /// </summary>
    public bool TelegramSoloEnHorario { get; init; }

    public IReadOnlyList<ThresholdRule> ForMetric(string metric) => metric switch
    {
        "queue" => Queue,
        "failed" => Failed,
        "missingHost" => MissingHost,
        "conn" => Conn,
        "silent" => Silent,
        "reprints" => Reprints,
        _ => Array.Empty<ThresholdRule>()
    };
}

/// <summary>
/// Horario de tienda común (hora de negocio) para «tienda sin recibir». Días ISO (1 = lunes …
/// 7 = domingo); Desde/Hasta "HH:mm", Desde &lt; Hasta (no cruza la medianoche).
/// </summary>
public sealed record HorarioTienda(IReadOnlyList<int> Dias, string Desde, string Hasta)
{
    public static HorarioTienda Default { get; } = new(new[] { 1, 2, 3, 4, 5, 6 }, "09:00", "21:00");

    public static bool TryHora(string? valor, out TimeOnly hora) =>
        TimeOnly.TryParseExact(valor, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out hora);

    /// <summary>Si ahora (hora de negocio) está dentro del horario: día y hora.</summary>
    public bool EstaAbierto(DateTimeOffset ahoraUtc, TimeZoneInfo zona)
    {
        if (!TryHora(Desde, out var desde) || !TryHora(Hasta, out var hasta))
            return false;
        var local = TimeZoneInfo.ConvertTime(ahoraUtc, zona);
        var dia = local.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)local.DayOfWeek;
        var hora = TimeOnly.FromDateTime(local.DateTime);
        return Dias.Contains(dia) && hora >= desde && hora < hasta;
    }

    /// <summary>
    /// Minutos de horario transcurridos desde el último trabajo recibido, o null si ahora está fuera
    /// de horario o la tienda no ha recibido nunca. Si el último llegó antes de abrir hoy, se cuenta
    /// desde la apertura: a primera hora no salta por lo que no llegó de noche.
    /// </summary>
    public int? MinutosSinRecibir(DateTimeOffset? ultimoUtc, DateTimeOffset ahoraUtc, TimeZoneInfo zona)
    {
        if (ultimoUtc is null || !EstaAbierto(ahoraUtc, zona) || !TryHora(Desde, out var desde))
            return null;

        var local = TimeZoneInfo.ConvertTime(ahoraUtc, zona);
        var aperturaLocal = local.Date + desde.ToTimeSpan();
        var aperturaUtc = new DateTimeOffset(aperturaLocal, zona.GetUtcOffset(aperturaLocal));
        var desdeCuando = ultimoUtc.Value > aperturaUtc ? ultimoUtc.Value : aperturaUtc;
        return (int)Math.Max(0, (ahoraUtc - desdeCuando).TotalMinutes);
    }
}
