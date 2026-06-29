namespace ImpresorasService.Domain.Entities;

/// <summary>
/// Registra el último estado de salud conocido por tienda.
/// Permite detectar transiciones (healthy→critical) sin enviar notificaciones duplicadas.
/// </summary>
public class StoreAlertState
{
    public int StoreId { get; set; }

    /// <summary>Último estado calculado: "healthy", "warning" o "critical".</summary>
    public string LastHealth { get; set; } = "healthy";

    /// <summary>Estado que se usó en la última notificación enviada.</summary>
    public string? NotifiedHealth { get; set; }

    /// <summary>Cuándo se envió la última notificación (null = nunca).</summary>
    public DateTimeOffset? NotifiedAtUtc { get; set; }

    /// <summary>Cuándo se comprobó por última vez el estado de esta tienda.</summary>
    public DateTimeOffset CheckedAtUtc { get; set; }
}
