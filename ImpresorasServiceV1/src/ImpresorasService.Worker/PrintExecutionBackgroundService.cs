using ImpresorasService.Application.Abstractions;
using ImpresorasService.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Worker;

public sealed class PrintExecutionBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<PrintExecutionOptions> _options;
    private readonly ILogger<PrintExecutionBackgroundService> _logger;

    public PrintExecutionBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<PrintExecutionOptions> options,
        ILogger<PrintExecutionBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Print Execution worker iniciado. Polling cada {Seconds}s", _options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
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
