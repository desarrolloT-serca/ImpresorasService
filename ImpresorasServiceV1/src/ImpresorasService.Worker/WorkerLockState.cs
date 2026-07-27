namespace ImpresorasService.Worker;

/// <summary>
/// Estado en memoria, compartido por todos los BackgroundService del proceso, que indica si esta
/// instancia del Worker es la titular del lock (G4.1). Lo actualiza <see cref="WorkerLockBackgroundService"/>;
/// el resto de servicios solo lo leen para decidir si procesan este ciclo.
/// </summary>
public sealed class WorkerLockState
{
    public static readonly string InstanceId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    private volatile bool _isHolder;

    public bool IsHolder => _isHolder;

    internal void SetHolder(bool value) => _isHolder = value;
}
