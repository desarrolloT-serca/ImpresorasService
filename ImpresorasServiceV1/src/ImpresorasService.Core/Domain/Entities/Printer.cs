namespace ImpresorasService.Domain.Entities;

public class Printer
{
    public int PrinterId { get; set; }
    public string PrinterName { get; set; } = string.Empty;
    public string SpoolQueue { get; set; } = string.Empty;
    public string? Host { get; set; }
    public int StoreId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CapabilitiesJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    // Conectividad (telemetría). Actualizada por el Worker.
    public int ConnectionFailuresStreak { get; set; } = 0;
    public bool? LastConnectionOk { get; set; }
    public DateTimeOffset? LastConnectionCheckAtUtc { get; set; }
    public string? LastConnectionTransport { get; set; }
    public string? LastConnectionError { get; set; }

    // null = sin comprobar, true = soporta IPP, false = no soporta IPP
    public bool? IppSupported { get; set; }
}
