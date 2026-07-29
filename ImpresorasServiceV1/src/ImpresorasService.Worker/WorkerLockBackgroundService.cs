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

        while (!stoppingToken.IsCancellationRequested)
        {
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

            _state.SetHolder(acquired);

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.Value.HeartbeatIntervalSeconds)), stoppingToken);
        }
    }
}
