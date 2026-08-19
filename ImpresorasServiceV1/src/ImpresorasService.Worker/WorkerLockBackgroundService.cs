using ImpresorasService.Application.Abstractions;
using ImpresorasService.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Worker;

/// <summary>
/// G4.1 (docs/roadmapimpresoras.md Fase 2.1): mantiene el lock de instancia única del Worker.
/// Adquiere/renueva en bucle vía <see cref="IWorkerLockCoordinator"/> y publica el resultado en
/// <see cref="WorkerLockState"/>, que consultan el resto de BackgroundService antes de procesar.
/// </summary>
public sealed class WorkerLockBackgroundService : BackgroundService
{
    /// <summary>Ciclos consecutivos sin lock tras los que el fallo pasa de Warning a Error.</summary>
    private const int CyclesBeforeEscalating = 6;

    /// <summary>Cada cuántos ciclos se repite el Error mientras siga sin lock (evita inundar el log).</summary>
    private const int EscalatedRepeatEveryCycles = 60;

    /// <summary>
    /// Un Warning por ciclo se pierde: el 17/08/2026 el Worker estuvo horas sin procesar nada
    /// (ingesta, impresión, conectividad y alertas paradas) mientras el servicio figuraba en
    /// Running y /health respondía ok, porque el fallo del lock solo dejaba un Warning cada 10s.
    /// A partir de <see cref="CyclesBeforeEscalating"/> ciclos se escala a Error —nivel que la
    /// monitorización sí recoge— y luego se repite espaciado.
    /// </summary>
    internal static bool ShouldEscalate(int consecutiveFailures)
        => consecutiveFailures == CyclesBeforeEscalating
           || (consecutiveFailures > CyclesBeforeEscalating
               && consecutiveFailures % EscalatedRepeatEveryCycles == 0);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerLockState _state;
    private readonly IOptions<WorkerLockOptions> _options;
    private readonly ILogger<WorkerLockBackgroundService> _logger;

    public WorkerLockBackgroundService(
        IServiceScopeFactory scopeFactory,
        WorkerLockState state,
        IOptions<WorkerLockOptions> options,
        ILogger<WorkerLockBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _state = state;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker lock: instancia {InstanceId} intentando adquirir el lock.", WorkerLockState.InstanceId);

        var leaseSeconds = Math.Max(1, _options.Value.LeaseSeconds);
        var heartbeatSeconds = Math.Max(1, _options.Value.HeartbeatIntervalSeconds);

        // WorkerLockState caduca por su cuenta al vencer el lease: si el heartbeat no llega antes,
        // la instancia se queda sin procesar aunque conserve el lock en BD.
        if (heartbeatSeconds >= leaseSeconds)
            _logger.LogWarning(
                "Worker lock: HeartbeatIntervalSeconds ({Heartbeat}s) >= LeaseSeconds ({Lease}s). El holder quedará inactivo entre renovaciones; use un heartbeat claramente menor que el lease.",
                heartbeatSeconds, leaseSeconds);

        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_options.Value.Enabled)
            {
                _state.SetHolder(true, leaseSeconds);
                await Task.Delay(TimeSpan.FromSeconds(heartbeatSeconds), stoppingToken);
                continue;
            }

            bool acquired;
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<IWorkerLockCoordinator>();
                acquired = await coordinator.TryAcquireOrRenewAsync(WorkerLockState.InstanceId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Worker lock: fallo al adquirir/renovar; esta instancia queda inactiva este ciclo.");
                acquired = false;
            }

            if (acquired != _state.IsHolder)
            {
                if (acquired)
                    _logger.LogWarning("Worker lock: instancia {InstanceId} ADQUIRIÓ el lock — pasa a procesar.", WorkerLockState.InstanceId);
                else
                    _logger.LogWarning("Worker lock: instancia {InstanceId} PERDIÓ/NO OBTUVO el lock — deja de procesar.", WorkerLockState.InstanceId);
            }

            if (acquired)
            {
                consecutiveFailures = 0;
            }
            else
            {
                consecutiveFailures++;
                if (ShouldEscalate(consecutiveFailures))
                    _logger.LogError(
                        "Worker lock: {Seconds}s sin lock ({Cycles} ciclos seguidos). El Worker NO esta procesando NADA " +
                        "(ingesta, impresion, conectividad y alertas parados) aunque el servicio figure en ejecucion. " +
                        "Revisa los privilegios de la conexion sobre printer_worker_lock, o pon WorkerLock:Enabled=false " +
                        "si esta instalacion es de instancia unica.",
                        consecutiveFailures * heartbeatSeconds, consecutiveFailures);
            }

            _state.SetHolder(acquired, leaseSeconds);

            await Task.Delay(TimeSpan.FromSeconds(heartbeatSeconds), stoppingToken);
        }
    }
}
