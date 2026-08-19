using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Connectivity;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private readonly TimeZoneInfo _businessTimeZone;
    private readonly WorkerLockState _lockState;
    private readonly IDashboardThresholdRuleStore _ruleStore;

    public StoreHealthAlertBackgroundService(
        IServiceScopeFactory scopeFactory,
        ITelegramNotifier telegram,
        IOptions<TelegramOptions> telegramOptions,
        ILogger<StoreHealthAlertBackgroundService> logger,
        TimeProvider timeProvider,
        IConfiguration configuration,
        WorkerLockState lockState,
        IDashboardThresholdRuleStore ruleStore)
    {
        _scopeFactory = scopeFactory;
        _telegram = telegram;
        _telegramOptions = telegramOptions;
        _logger = logger;
        _timeProvider = timeProvider;
        _lockState = lockState;
        _ruleStore = ruleStore;
        // Misma clave que DashboardController (Api) — KPI-P2-004: dashboard y alertas deben usar
        // el mismo reloj de negocio, no que uno lea Europe/Madrid y el otro medianoche UTC.
        _businessTimeZone = BusinessTimeZoneClock.Resolve(configuration["Dashboard:BusinessTimeZone"], logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // G4.1: sin el lock de instancia única, esta réplica no evalúa/notifica (evita alertas Telegram duplicadas).
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

        // Umbrales legacy (2 niveles): siguen en BD, solo se usan aquí para el criterio OR de
        // connWarning/connCritical (mismo criterio que usaba PHP junto a la clasificación de
        // conectividad — ver LoadConnectivityStatsAsync en DashboardController.cs, Api).
        var thresholdRow = await db.DashboardThresholds.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, ct);

        int connWarnMin = thresholdRow?.ConnWarningFailuresMin ?? 2;
        int connCritMin = thresholdRow?.ConnCriticalFailuresMin ?? 3;

        // G5.3: reglas de severidad de 1-3 niveles — fuente única de verdad, compartida con la Api.
        var rules = await _ruleStore.LoadAsync(ct);

        // No usar const: EF plegaría `IsActive == true` a un booleano desnudo y HANA rechaza
        // `WHERE (col AND ...)`. Como variable se parametriza y genera `col = :param`.
        var activeOnly = true;
        var now = _timeProvider.GetUtcNow();

        var stores = await db.Stores.AsNoTracking()
            .Where(s => s.IsActive == activeOnly)
            .Select(s => new { s.StoreId, s.Name })
            .ToListAsync(ct);

        if (stores.Count == 0)
            return;

        // ── Snapshot en batch: una query por recurso, no por tienda (AUD-19) ──
        var storeIdList = stores.Select(s => s.StoreId).ToList();

        var printersByStore = (await db.Printers.AsNoTracking()
            .Where(p => p.IsActive == activeOnly && storeIdList.Contains(p.StoreId))
            .Select(p => new
            {
                p.StoreId, p.IsActive, p.LastConnectionOk,
                p.LastConnectionCheckAtUtc, p.LastConnectionError, p.ConnectionFailuresStreak,
            })
            .ToListAsync(ct))
            .GroupBy(p => p.StoreId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var queuedByStore = await db.PrintJobs.AsNoTracking()
            .Where(j => storeIdList.Contains(j.StoreId) && DashboardPrintJobPredicates.QueueStatuses.Contains(j.Status))
            .GroupBy(j => j.StoreId)
            .Select(g => new { StoreId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Count, ct);

        // Mismo predicado que DashboardController.BuildStoreRowsAsync (failedWindowStats).
        // Sin ventana (A-KPI-01): foto de estado actual, no evento — ver comentario original.
        var failedByStore = await db.PrintJobs.AsNoTracking()
            .Where(j => storeIdList.Contains(j.StoreId))
            .Where(DashboardPrintJobPredicates.FailedWithoutRetryCurrent)
            .GroupBy(j => j.StoreId)
            .Select(g => new { StoreId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Count, ct);

        // Sin AsNoTracking: EF debe detectar los cambios para SaveChanges.
        var alertStates = await db.StoreAlertStates
            .Where(s => storeIdList.Contains(s.StoreId))
            .ToDictionaryAsync(s => s.StoreId, ct);

        // ── Evaluación en memoria ─────────────────────────────────────────────
        const string hr = "—————————————————";
        var localTime = TimeZoneInfo.ConvertTime(now.UtcDateTime, _businessTimeZone);
        var ts = localTime.ToString("dd/MM HH:mm");

        foreach (var store in stores)
        {
            var printers = printersByStore.GetValueOrDefault(store.StoreId) ?? [];
            var connected = printers.Count;
            var missingHost = 0;
            var connWarn = 0;
            var connCrit = 0;
            var connMaxStreak = 0;

            foreach (var p in printers)
            {
                var descriptor = PrinterConnectivityState.FromSnapshot(
                    p.IsActive, p.LastConnectionOk, p.LastConnectionCheckAtUtc,
                    p.LastConnectionError, p.ConnectionFailuresStreak);

                if (descriptor.ConnectivityStatus == PrinterConnectivityState.StatusNoHost)
                    missingHost++;

                connMaxStreak = Math.Max(connMaxStreak, p.ConnectionFailuresStreak);

                if (descriptor.ConnectivitySeverity == PrinterConnectivityState.SeverityCritical || p.ConnectionFailuresStreak >= connCritMin)
                    connCrit++;
                else if ((descriptor.ConnectivitySeverity == PrinterConnectivityState.SeverityWarning && descriptor.ConnectivityStatus != PrinterConnectivityState.StatusNoHost)
                         || p.ConnectionFailuresStreak >= connWarnMin)
                    connWarn++;
            }

            queuedByStore.TryGetValue(store.StoreId, out var queued);
            failedByStore.TryGetValue(store.StoreId, out var failed);

            var (health, reason) = StoreHealthEvaluator.Compute(
                connected, queued, failed, missingHost, connMaxStreak, connCrit, connWarn, rules);

            if (!alertStates.TryGetValue(store.StoreId, out var alertState))
            {
                alertState = new StoreAlertState { StoreId = store.StoreId, CheckedAtUtc = now };
                db.StoreAlertStates.Add(alertState);
                alertStates[store.StoreId] = alertState;
            }

            var previousNotifiedHealth = alertState.NotifiedHealth ?? "healthy";
            bool isAlertLevel = SeverityReached(health, minSeverity);
            bool wasAlertLevel = SeverityReached(previousNotifiedHealth, minSeverity);
            bool isEscalation = isAlertLevel && wasAlertLevel
                && SeverityRank(health) > SeverityRank(previousNotifiedHealth);

            string? message = null;

            if (isAlertLevel && (!wasAlertLevel || isEscalation))
            {
                var icon = health == "critical" ? "🔴" : "🟡";
                var label = health == "critical" ? "CRÍTICA" : "WARNING";
                message =
                    $"{hr}\n\n" +
                    $"{icon} <b>{label}</b> · <b>{store.Name}</b> <code>#{store.StoreId}</code>\n" +
                    $"{reason}\n\n" +
                    $"📦 Cola <b>{queued}</b> · ❌ Fallos <b>{failed}</b>\n" +
                    $"🕒 {ts}\n\n" +
                    $"{hr}";
            }
            else if (!isAlertLevel && wasAlertLevel && notifyOnRecovery)
            {
                message =
                    $"{hr}\n\n" +
                    $"🟢 <b>RECUPERADA</b> · <b>{store.Name}</b> <code>#{store.StoreId}</code>\n" +
                    $"<code>{previousNotifiedHealth}</code> → <b>saludable</b>\n\n" +
                    $"🕒 {ts}\n\n" +
                    $"{hr}";
            }

            alertState.LastHealth = health;
            alertState.CheckedAtUtc = now;
            // NotifiedHealth debe seguir a la salud real aunque no se emita mensaje. Si solo se
            // actualizara al notificar, con NotifyOnRecovery=false una tienda que se recupera deja
            // NotifiedHealth="critical" para siempre: la siguiente caída ya no es transición y la
            // tienda queda permanentemente muda.
            alertState.NotifiedHealth = health;

            if (message is not null)
            {
                var notifiedAtBeforeSend = alertState.NotifiedAtUtc;

                // Fase 1.7: persistir ANTES de enviar — evita reenvío si el proceso cae entre save y send.
                alertState.NotifiedAtUtc = now;
                await db.SaveChangesAsync(ct);

                var delivered = await _telegram.SendAlertAsync(message, ct, store.StoreId);
                if (delivered)
                {
                    _logger.LogInformation("Alerta Telegram enviada para tienda {StoreId} ({Name}): {Health}.",
                        store.StoreId, store.Name, health);
                }
                else
                {
                    // Se deshace el avance del estado notificado para que el próximo ciclo vuelva a
                    // ver esto como una transición y lo reintente. Sin esto, una caída momentánea de
                    // Telegram (o un chat mal configurado) perdía la alerta para siempre: el estado
                    // quedaba marcado como notificado y la siguiente comprobación ya no era cambio.
                    //
                    // Solo se revierte cuando SABEMOS que no se entregó. Si el proceso muere entre el
                    // guardado y el envío, el estado ya avanzó y no se reenvía — que es justo lo que
                    // buscaba persistir antes de enviar.
                    //
                    // ponytail: reintento por ciclo, no un outbox. No hay estado por chat ni backoff,
                    // así que con Telegram caído se reintenta cada CheckIntervalMinutes hasta que
                    // entre. Si hiciera falta granularidad por destinatario, eso sí pide tabla (AUD-14).
                    alertState.NotifiedHealth = previousNotifiedHealth;
                    alertState.NotifiedAtUtc = notifiedAtBeforeSend;

                    _logger.LogWarning(
                        "Alerta Telegram NO entregada para tienda {StoreId} ({Name}): {Health}. Ningún chat la aceptó " +
                        "(sin chats activos o todos los envíos fallaron); se reintentará en el próximo ciclo.",
                        store.StoreId, store.Name, health);
                }
            }
        }

        // Un SaveChanges final para tiendas sin alerta (LastHealth/CheckedAtUtc acumulados)
        await db.SaveChangesAsync(ct);
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
