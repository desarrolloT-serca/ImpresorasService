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
    // Desde cuándo dura el estado y los motivos de ahora: el «Desde» de sus incidencias (docs/72, mejora 3).
    public DateTimeOffset? IppStateSinceUtc { get; set; }
    // Para la ficha (docs/72, mejora 6): firmware, desde cuándo está encendida y bandejas (JSON).
    public string? IppFirmware { get; set; }
    public DateTimeOffset? IppUpSinceUtc { get; set; }
    public string? IppTraysJson { get; set; }
}

/// <summary>
/// Nivel de un consumible cuando cambia (docs/72, mejora 7): para prever cuándo se acaba. Solo se guarda al
/// cambiar, así son pocas filas; las de más de <c>TonerReadings.Retencion</c> se borran.
/// </summary>
public class TonerReading
{
    public long Id { get; set; }
    public int PrinterId { get; set; }
    /// <summary>Posición del consumible en marker-* (0 = el primero; en las B/N, el único).</summary>
    public int Marker { get; set; }
    public int Level { get; set; }
    public DateTimeOffset ReadAtUtc { get; set; }
}
