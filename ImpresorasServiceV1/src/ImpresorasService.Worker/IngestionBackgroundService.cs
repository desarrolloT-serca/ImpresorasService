using ImpresorasService.Application.Options;
using ImpresorasService.Application.Services;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Worker;

public class IngestionBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<IngestionOptions> _options;
    private readonly ILogger<IngestionBackgroundService> _logger;

    public IngestionBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<IngestionOptions> options,
        ILogger<IngestionBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Ingestion worker iniciado. Polling cada {Seconds}s", _options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
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
