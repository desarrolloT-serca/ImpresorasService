using ImpresorasService.Application.Abstractions;
using ImpresorasService.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Worker;

public sealed class PrintExecutionBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<PrintExecutionOptions> _options;
    private readonly ILogger<PrintExecutionBackgroundService> _logger;
    private readonly WorkerLockState _lockState;

    public PrintExecutionBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<PrintExecutionOptions> options,
        ILogger<PrintExecutionBackgroundService> logger,
        WorkerLockState lockState)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
        _lockState = lockState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Print Execution worker iniciado. Polling cada {Seconds}s", _options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            // G4.1: sin el lock de instancia única, esta réplica no envía al spooler (evita impresiones duplicadas).
            if (!_lockState.IsHolder)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var executionService = scope.ServiceProvider.GetRequiredService<IPrintExecutionService>();
                int processed = await executionService.ExecuteBatchAsync(_options.Value.BatchSize, stoppingToken);

                if (processed > 0)
                    _logger.LogInformation("Lote de impresión ejecutado. Trabajos procesados: {Processed}", processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado en ciclo de impresión.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds), stoppingToken);
        }
    }
}
