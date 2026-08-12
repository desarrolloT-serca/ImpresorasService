namespace ImpresorasService.Infrastructure.Options;

public sealed class WorkerLockOptions
{
    public const string SectionName = "WorkerLock";

    /// <summary>Si es false, omite el lock de BD y asume que esta instancia es el único holder. Usar solo en entornos de instancia única sin la tabla printer_worker_lock provisionada.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Segundos sin renovar el heartbeat tras los que el lock se considera expirado y otra instancia puede tomarlo.</summary>
    public int LeaseSeconds { get; set; } = 30;

    /// <summary>Intervalo en segundos entre intentos de adquisición/renovación del lock.</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 10;
}
