using ImpresorasService.Application.Options;
using ImpresorasService.Application.Services;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Worker;

public class IngestionBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<IngestionOptions> _options;
    private readonly ILogger<IngestionBackgroundService> _logger;
    private readonly WorkerLockState _lockState;

    public IngestionBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<IngestionOptions> options,
        ILogger<IngestionBackgroundService> logger,
        WorkerLockState lockState)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
        _lockState = lockState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Ingestion worker iniciado. Polling cada {Seconds}s", _options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            // G4.1: sin el lock de instancia única, esta réplica no procesa (evita ingesta duplicada).
            if (!_lockState.IsHolder)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            try
            {
                using IServiceScope scope = _serviceProvider.CreateScope();
                var ingestionService = scope.ServiceProvider.GetRequiredService<IngestionService>();
                int inserted = await ingestionService.IngestBatchAsync(_options.Value.BatchSize, stoppingToken);

                _logger.LogInformation("Lote de ingesta ejecutado. Trabajos insertados: {Inserted}", inserted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado en ciclo de ingesta.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds), stoppingToken);
        }
    }
}
