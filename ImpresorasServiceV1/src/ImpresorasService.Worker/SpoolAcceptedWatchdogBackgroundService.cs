using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Worker;

public sealed class SpoolAcceptedWatchdogBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PrintExecutionOptions> _options;
    private readonly ILogger<SpoolAcceptedWatchdogBackgroundService> _logger;

    public SpoolAcceptedWatchdogBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<PrintExecutionOptions> options,
        ILogger<SpoolAcceptedWatchdogBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
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
                _logger.LogWarning(ex, "Fallo en watchdog de SpoolAccepted.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(Math.Max(1, _options.Value.SpoolAcceptedWatchIntervalSeconds)),
                stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var maxAgeSeconds = Math.Max(1, _options.Value.SpoolAcceptedMaxAgeSeconds);
        var thresholdUtc = now - TimeSpan.FromSeconds(maxAgeSeconds);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

        // SQLite + EF Core: `DateTimeOffset` en ORDER BY no está soportado en SQL.
        // Filtramos por estado en SQL; el umbral de antigüedad y el orden temporal van en memoria.
        var windowLimit = Math.Max(200, _options.Value.SpoolAcceptedWatchBatchSize * 50);
        var windowCandidates = await db.PrintJobs
            .AsNoTracking()
            .Where(j => j.Status == PrintJobStatus.SpoolAccepted)
            .Take(windowLimit)
            .ToListAsync(ct);

        var candidates = windowCandidates
            .Where(j => j.UpdatedAtUtc <= thresholdUtc)
            .OrderBy(j => j.UpdatedAtUtc)
            .Take(Math.Max(1, _options.Value.SpoolAcceptedWatchBatchSize))
            .ToList();

        if (candidates.Count == 0)
            return;

        var timeoutCode = "SPOOL_ACCEPTED_TIMEOUT";

        foreach (var job in candidates)
        {
            var oldStatus = job.Status;
            job.Status = PrintJobStatus.PrintedUnknown;
            job.LastErrorCode = timeoutCode;
            job.LastErrorMessage = $"SpoolAccepted sin confirmación tras {maxAgeSeconds} segundos (watchdog).";
            job.NextRetryAtUtc = null;
            job.UpdatedAtUtc = now;

            db.PrintJobs.Update(job);
            await db.PrintJobEvents.AddAsync(new PrintJobEvent
            {
                JobId = job.JobId,
                EventType = "StatusChanged",
                OldStatus = oldStatus,
                NewStatus = PrintJobStatus.PrintedUnknown,
                ErrorCode = timeoutCode,
                Message = job.LastErrorMessage,
                ActorType = "system",
                OccurredAtUtc = now
            }, ct);
        }

        try
        {
            await db.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Watchdog reclamó {Count} jobs de SpoolAccepted -> PrintedUnknown (maxAgeSeconds={MaxAge}).",
                candidates.Count,
                maxAgeSeconds);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Si alguien más actualizó el job, lo ignoramos: no queremos bloquear el flujo.
            _logger.LogInformation("Watchdog: se produjo concurrencia; reintento en el siguiente ciclo.");
        }
    }
}

