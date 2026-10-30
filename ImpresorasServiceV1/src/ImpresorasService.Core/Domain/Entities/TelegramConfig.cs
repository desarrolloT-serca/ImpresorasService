namespace ImpresorasService.Domain.Entities;

public class TelegramConfig
{
    public int Id { get; set; }

    /// <summary>Severidad mínima que dispara una notificación: "warning" o "critical".</summary>
    public string MinSeverity { get; set; } = "critical";

    /// <summary>Si true, también notifica cuando una tienda pasa de alerta a estado saludable.</summary>
    public bool NotifyOnRecovery { get; set; } = true;

    /// <summary>Intervalo en minutos entre ciclos de chequeo de salud de tiendas.</summary>
    public int CheckIntervalMinutes { get; set; } = 5;

    /// <summary>Token del bot puesto desde la pantalla; manda sobre Telegram:BotToken. Null = el de la config.</summary>
    public string? BotToken { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
