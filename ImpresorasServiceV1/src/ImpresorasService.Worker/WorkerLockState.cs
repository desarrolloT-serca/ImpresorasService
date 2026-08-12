namespace ImpresorasService.Worker;

/// <summary>
/// Estado en memoria, compartido por todos los BackgroundService del proceso, que indica si esta
/// instancia del Worker es la titular del lock (G4.1). Lo actualiza <see cref="WorkerLockBackgroundService"/>;
/// el resto de servicios solo lo leen para decidir si procesan este ciclo.
/// </summary>
public sealed class WorkerLockState
{
    public static readonly string InstanceId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    // Instante (reloj monótono, ms desde el arranque) hasta el que la última renovación conocida
    // sigue siendo válida. 0 = no somos holder.
    private long _holderUntilTick;

    /// <summary>
    /// True solo si la última renovación del lease sigue vigente. La caducidad la lleva el dato,
    /// no el bucle que lo refresca: si el proceso se congela (pausa GC larga, suspensión de VM,
    /// Task.Delay retrasado), este flag pasa a false por sí solo en cuanto vence el lease, en vez
    /// de quedarse en true mientras otra instancia ya adquirió el lock y ambas envían al spooler.
    /// Reloj monótono a propósito: un ajuste de la hora del sistema no debe conceder ni retirar el lock.
    /// </summary>
    public bool IsHolder => Volatile.Read(ref _holderUntilTick) > Environment.TickCount64;

    internal void SetHolder(bool value, int leaseSeconds) => Volatile.Write(
        ref _holderUntilTick,
        value ? Environment.TickCount64 + Math.Max(1, leaseSeconds) * 1000L : 0);
}
