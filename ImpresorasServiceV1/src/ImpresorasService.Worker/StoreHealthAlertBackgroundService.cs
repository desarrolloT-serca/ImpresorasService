using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
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

public sealed class StoreHealthAlertBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramNotifier _telegram;
    private readonly IOptions<TelegramOptions> _telegramOptions;
    private readonly ILogger<StoreHealthAlertBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;

    private static readonly TimeZoneInfo _spainTz =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Madrid");

    // Debe mantenerse idéntico a DashboardController.QueueStatuses.
    private static readonly PrintJobStatus[] QueueStatuses =
    [
        PrintJobStatus.Pending, PrintJobStatus.Routed,
        PrintJobStatus.Printing, PrintJobStatus.RetryScheduled
    ];

    public StoreHealthAlertBackgroundService(
        IServiceScopeFactory scopeFactory,
        ITelegramNotifier telegram,
        IOptions<TelegramOptions> telegramOptions,
        ILogger<StoreHealthAlertBackgroundService> logger,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _telegram = telegram;
        _telegramOptions = telegramOptions;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

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
                _logger.LogWarning(ex, "Fallo en el servicio de alertas de tiendas.");
            }

            var intervalMinutes = await GetCheckIntervalAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, intervalMinutes)), stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        if (!_telegramOptions.Value.Enabled)
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

        var config = await db.TelegramConfigs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (config is null)
            return;

        var minSeverity = config.MinSeverity.Trim().ToLowerInvariant();
        var notifyOnRecovery = config.NotifyOnRecovery;

        var thresholdRow = await db.DashboardThresholds.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, ct);

        int warnQueue = thresholdRow?.WarningQueueMin ?? 10;
        int critQueue = thresholdRow?.CriticalQueueMin ?? 30;
        int warnFailed = thresholdRow?.WarningFailedWithoutRetryMin ?? 1;
        int critFailed = thresholdRow?.CriticalFailedWithoutRetryMin ?? 5;
        int missingHostMin = thresholdRow?.MissingHostMin ?? 1;
        int connWarnMin = thresholdRow?.ConnWarningFailuresMin ?? 2;
        int connCritMin = thresholdRow?.ConnCriticalFailuresMin ?? 3;

        var activeOnly = true;
        var stores = await db.Stores.AsNoTracking()
            .Where(s => s.IsActive == activeOnly)
            .Select(s => new { s.StoreId, s.Name })
            .ToListAsync(ct);

        var now = _timeProvider.GetUtcNow();
        var windowStart = now.Date;

        foreach (var store in stores)
        {
            var printers = await db.Printers.AsNoTracking()
                .Where(p => p.IsActive == activeOnly && p.StoreId == store.StoreId)
                .Select(p => new
                {
                    p.Host,
                    p.SpoolQueue,
                    p.ConnectionFailuresStreak,
                })
                .ToListAsync(ct);

            var connected = printers.Count;
            var missingHost = printers.Count(p => string.IsNullOrEmpty(p.Host) && !HasImplicitHost(p.SpoolQueue));
            var connWarn = printers.Count(p => p.ConnectionFailuresStreak >= connWarnMin);
            var connCrit = printers.Count(p => p.ConnectionFailuresStreak >= connCritMin);

            var queued = await db.PrintJobs.AsNoTracking()
                .CountAsync(j => j.StoreId == store.StoreId && QueueStatuses.Contains(j.Status), ct);

            // Alineado con DashboardController.BuildStoreRowsAsync: failedWindowStats usa UpdatedAtUtc
            // y cuenta ErrorFinal más jobs con múltiples intentos aún sin éxito.
            var updatedJobs = await db.PrintJobs.AsNoTracking()
                .Where(j => j.StoreId == store.StoreId && j.UpdatedAtUtc >= windowStart)
                .Select(j => new { j.Status, j.AttemptCount })
                .ToListAsync(ct);

            var failed = updatedJobs.Count(j => j.Status == PrintJobStatus.ErrorFinal || IsFailedAfterRetry(j.Status, j.AttemptCount));

            var (health, reason) = StoreHealthEvaluator.Compute(
                connected, queued, failed, missingHost, connWarn, connCrit,
                warnQueue, critQueue, warnFailed, critFailed, missingHostMin, connWarnMin, connCritMin,
                thresholdRow?.ConnCriticalSeverity ?? "critical",
                thresholdRow?.FailedCriticalSeverity ?? "critical",
                thresholdRow?.QueueCriticalSeverity ?? "critical",
                thresholdRow?.ConnWarningSeverity ?? "warning",
                thresholdRow?.MissingHostSeverity ?? "warning",
                thresholdRow?.FailedWarningSeverity ?? "warning",
                thresholdRow?.QueueWarningSeverity ?? "warning");

            await ProcessStoreAlertAsync(db, store.StoreId, store.Name,
                health, reason, queued, failed, minSeverity, notifyOnRecovery, now, ct);
        }
    }

    private async Task ProcessStoreAlertAsync(
        ImpresorasDbContext db,
        int storeId,
        string storeName,
        string currentHealth,
        string reason,
        int queued,
        int failed,
        string minSeverity,
        bool notifyOnRecovery,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var alertState = await db.StoreAlertStates
            .SingleOrDefaultAsync(s => s.StoreId == storeId, ct);

        if (alertState is null)
        {
            alertState = new StoreAlertState { StoreId = storeId, CheckedAtUtc = now };
            await db.StoreAlertStates.AddAsync(alertState, ct);
        }

        var previousNotifiedHealth = alertState.NotifiedHealth ?? "healthy";
        bool isAlertLevel = SeverityReached(currentHealth, minSeverity);
        bool wasAlertLevel = SeverityReached(previousNotifiedHealth, minSeverity);
        bool isEscalation = isAlertLevel && wasAlertLevel
            && SeverityRank(currentHealth) > SeverityRank(previousNotifiedHealth);

        var spainTime = TimeZoneInfo.ConvertTime(now.UtcDateTime, _spainTz);
        var ts = spainTime.ToString("dd/MM HH:mm");

        string? message = null;

        const string hr = "—————————————————";

        if (isAlertLevel && (!wasAlertLevel || isEscalation))
        {
            var icon = currentHealth == "critical" ? "🔴" : "🟡";
            var label = currentHealth == "critical" ? "CRÍTICA" : "WARNING";
            message =
                $"{hr}\n\n" +
                $"{icon} <b>{label}</b> · <b>{storeName}</b> <code>#{storeId}</code>\n" +
                $"{reason}\n\n" +
                $"📦 Cola <b>{queued}</b> · ❌ Fallos <b>{failed}</b>\n" +
                $"🕒 {ts}\n\n" +
                $"{hr}";
        }
        else if (!isAlertLevel && wasAlertLevel && notifyOnRecovery)
        {
            message =
                $"{hr}\n\n" +
                $"🟢 <b>RECUPERADA</b> · <b>{storeName}</b> <code>#{storeId}</code>\n" +
                $"<code>{previousNotifiedHealth}</code> → <b>saludable</b>\n\n" +
                $"🕒 {ts}\n\n" +
                $"{hr}";
        }

        alertState.LastHealth = currentHealth;
        alertState.CheckedAtUtc = now;

        if (message is not null)
        {
            // Fase 1.7: persistir el nuevo NotifiedHealth ANTES de enviar. Un crash entre el
            // envío y el guardado reenviaba la misma alerta en el siguiente ciclo (spam); con
            // este orden, en el peor caso una notificación no llega a enviarse pero el estado
            // queda consistente (no hay reenvío duplicado).
            alertState.NotifiedHealth = currentHealth;
            alertState.NotifiedAtUtc = now;
            await db.SaveChangesAsync(ct);

            await _telegram.SendAlertAsync(message, ct, storeId);
            _logger.LogInformation("Alerta Telegram enviada para tienda {StoreId} ({Name}): {Health}.",
                storeId, storeName, currentHealth);
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private static bool SeverityReached(string health, string minSeverity) => minSeverity switch
    {
        "warning" => health is "warning" or "critical",
        "critical" => health == "critical",
        _ => health == "critical"
    };

    private static int SeverityRank(string health) => health switch
    {
        "warning" => 1,
        "critical" => 2,
        _ => 0
    };

    private static bool IsFailedAfterRetry(PrintJobStatus status, int attemptCount)
        => status != PrintJobStatus.RetryScheduled
            && status != PrintJobStatus.SpoolAccepted
            && status != PrintJobStatus.Printing
            && status != PrintJobStatus.PrintedConfirmed
            && status != PrintJobStatus.PrintedUnknown
            && attemptCount > 1;

    private static bool HasImplicitHost(string? spoolQueue)
        => !string.IsNullOrEmpty(spoolQueue)
            && spoolQueue.StartsWith(@"\\", StringComparison.Ordinal);

    private async Task<int> GetCheckIntervalAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            var interval = await db.TelegramConfigs.AsNoTracking()
                .Where(c => c.Id == 1)
                .Select(c => c.CheckIntervalMinutes)
                .SingleOrDefaultAsync(ct);
            return interval > 0 ? interval : 5;
        }
        catch
        {
            return 5;
        }
    }
}
