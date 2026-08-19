using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Worker;

/// <summary>
/// Libera el PDF de los trabajos que llevan cerrados más tiempo del plazo de retención.
/// Conserva la fila, el hash y los metadatos: lo único que se pierde es la capacidad de volver a
/// imprimir ese documento, que a esas alturas ya no es una operación esperada.
/// La regla de qué se libera está en <see cref="PdfRetention"/>; aquí solo el hosting.
/// </summary>
public sealed class PdfRetentionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PdfRetentionOptions> _options;
    private readonly ILogger<PdfRetentionBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly WorkerLockState _lockState;

    public PdfRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<PdfRetentionOptions> options,
        ILogger<PdfRetentionBackgroundService> logger,
        TimeProvider timeProvider,
        WorkerLockState lockState)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider;
        _lockState = lockState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation(
                "Retencion de PDF desactivada (PdfRetention:Enabled=false). Los PDF se conservan indefinidamente.");
            return;
        }

        _logger.LogInformation(
            "Retencion de PDF activa: se liberara el PDF de los trabajos cerrados hace mas de {Days} dias, cada {Hours} h.",
            _options.Value.RetentionDays, _options.Value.IntervalHours);

        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Sin el lock de instancia unica esta replica no escribe (mismo criterio que el resto).
            if (!_lockState.IsHolder)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // El fallo tipico es que pdf_blob siga siendo NOT NULL en el esquema.
                _logger.LogWarning(ex,
                    "Fallo en la retencion de PDF. Si el error viene de una restriccion NOT NULL, aplique " +
                    "scripts/sql/migrate_pdf_blob_nullable.sql en el esquema.");
            }

            await Task.Delay(TimeSpan.FromHours(Math.Max(1, _options.Value.IntervalHours)), stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

        var cutoff = _timeProvider.GetUtcNow().AddDays(-Math.Max(1, _options.Value.RetentionDays));
        var released = await PdfRetention.ReleaseExpiredPdfsAsync(db, cutoff, _options.Value.BatchSize, ct);
        var releasedFromSource = await PdfRetention.ReleaseExpiredSourcePdfsAsync(db, cutoff, _options.Value.BatchSize, ct);

        if (released > 0 || releasedFromSource > 0)
            _logger.LogInformation(
                "Retencion de PDF anterior a {Cutoff:yyyy-MM-dd}: liberados {Released} trabajos cerrados y {ReleasedFromSource} filas de origen ya procesadas. Se conservan fila, hash y metadatos.",
                cutoff, released, releasedFromSource);
    }
}
