namespace ImpresorasService.Domain.Entities;

public class Printer
{
    public int PrinterId { get; set; }
    public string PrinterName { get; set; } = string.Empty;
    // Cola de Windows: la alternativa cuando no se puede imprimir por IP (Host). Sin ella, solo por IP.
    public string? SpoolQueue { get; set; }
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
    // Desde cuándo falla sin interrupción (sin conexión o sin host); null mientras responde.
    public DateTimeOffset? ConnectionFailingSinceUtc { get; set; }

    // null = sin comprobar, true = soporta IPP, false = no soporta IPP
    public bool? IppSupported { get; set; }

    // Último estado leído por IPP (Worker, cada 30 s). Se queda la última lectura aunque luego deje de
    // responder: IppCheckedAtUtc dice de cuándo es. Se borra al cambiar de host (ERP_SERCA docs/72, R1).
    public int? IppState { get; set; }
    public string? IppStateReasons { get; set; }
    public string? IppStateMessage { get; set; }
    public int? IppQueuedJobs { get; set; }
    public string? IppMarkersJson { get; set; }
    public string? IppMakeAndModel { get; set; }
    public string? IppMoreInfoUrl { get; set; }
    public DateTimeOffset? IppCheckedAtUtc { get; set; }
}
