using System.Security.Claims;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Connectivity;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Authorize(Policy = "EmployeeOrAbove")]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly ImpresorasDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _businessTimeZone;
    private readonly ILogger<DashboardController> _logger;
    private readonly IDashboardThresholdRuleStore _ruleStore;
    private const int DefaultWarningQueueMin = 10;
    private const int DefaultCriticalQueueMin = 30;
    private const int DefaultWarningFailedWithoutRetryMin = 1;
    private const int DefaultCriticalFailedWithoutRetryMin = 5;
    private const int DefaultMissingHostMin = 1;
    private const int DefaultConnWarningFailuresMin = 2;
    private const int DefaultConnCriticalFailuresMin = 3;

    public DashboardController(
        ImpresorasDbContext dbContext, TimeProvider timeProvider, IConfiguration configuration,
        ILogger<DashboardController> logger, IDashboardThresholdRuleStore ruleStore)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
        _ruleStore = ruleStore;
        // Compartido con StoreHealthAlertBackgroundService (Worker) — KPI-P2-004, mismo reloj de negocio.
        _businessTimeZone = BusinessTimeZoneClock.Resolve(configuration["Dashboard:BusinessTimeZone"], _logger);
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(
        [FromQuery] string? window,
        [FromQuery] int? storeId,
        CancellationToken cancellationToken)
    {
        var thresholds = await GetThresholdsAsync(cancellationToken);
        var effectiveStoreId = IsAdmin() ? storeId : GetCurrentUserStoreId();
        var now = _timeProvider.GetUtcNow();
        var fromUtc = ResolveWindowStartUtc(window, now);

        var activeOnly = true;
        var stores = _dbContext.Stores.AsNoTracking().Where(x => x.IsActive == activeOnly);
        var printers = _dbContext.Printers.AsNoTracking().Where(x => x.IsActive == activeOnly);
        // El dashboard operativo solo muestra tiendas activas; excluye histórico de tiendas dadas de baja.
        // Los ids se materializan en vez de usar un Any() correlacionado: el provider de HANA traduce
        // ese EXISTS a SQL inválido ("WHERE ( 1 AS X FROM DUMMY WHERE EXISTS ..."), que tumbaba el
        // endpoint entero con un 500. Mismo patrón que StoreHealthAlertBackgroundService.
        var activeStoreIds = await stores
            .Select(x => x.StoreId)
            .ToListAsync(cancellationToken);
        var jobs = _dbContext.PrintJobs.AsNoTracking()
            .Where(x => activeStoreIds.Contains(x.StoreId));

        if (effectiveStoreId.HasValue)
        {
            jobs = jobs.Where(x => x.StoreId == effectiveStoreId.Value);
            stores = stores.Where(x => x.StoreId == effectiveStoreId.Value);
            printers = printers.Where(x => x.StoreId == effectiveStoreId.Value);
        }

        // received: cohorte de creados en la ventana (foto de qué entró). failedWithoutRetryCurrent:
        // foto actual de fallos sin reenvío — SIN ventana (A-KPI-01, auditoria-integral-2026-07-21.md):
        // un ErrorFinal es terminal, su UpdatedAtUtc no vuelve a moverse, así que filtrarlo por ventana
        // lo hacía "desaparecer" al cruzar medianoche aunque el job siguiera roto. printed/failed:
        // eventos de negocio (PrintJobEvents), contados una sola vez por job — KPI-P1-001/002,
        // docs/prompt-roadmap-kpi-dashboard.md. Acotada también por arriba (<= now), igual que PHP
        // (resolveWindowRange) — VAL-P3-009.
        var jobsInWindow = jobs.Where(x => x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc <= now);

        var (printedRows, failedRows) = await LoadPrintedAndFailedAsync(jobs, fromUtc, now, cancellationToken);

        // BuildStoreRowsAsync ya agrega received/queue/failed por tienda en HANA;
        // sumar en memoria evita 5 COUNT queries adicionales (AUD-18).
        var rules = await _ruleStore.LoadAsync(cancellationToken);
        var (storeRows, alerts) = await BuildStoreRowsAsync(stores, printers, jobsInWindow, jobs, printedRows, failedRows, thresholds, rules, cancellationToken);

        var kpis = new
        {
            received = storeRows.Sum(r => r.Received),
            printed = printedRows.Count,
            failed = failedRows.Count,
            queueCurrent = storeRows.Sum(r => r.QueuedCurrent),
            failedWithoutRetryCurrent = storeRows.Sum(r => r.FailedWithoutRetryCurrent),
            activePrinters = storeRows.Sum(r => r.ConnectedPrinters),
            activeStores = storeRows.Count
        };

        // Observabilidad de negocio (A-ARCH-05, G4.3): sin backend de métricas (OTel/Prometheus) en
        // el proyecto, se usa logging estructurado — mismo patrón que IngestionService/StoreHealthAlertBackgroundService.
        _logger.LogInformation(
            "Dashboard overview KPIs. window={Window} storeId={StoreId} received={Received} printed={Printed} failed={Failed} queueCurrent={QueueCurrent} failedWithoutRetryCurrent={FailedWithoutRetryCurrent} activePrinters={ActivePrinters} activeStores={ActiveStores}",
            NormalizeWindow(window), effectiveStoreId, kpis.received, kpis.printed, kpis.failed,
            kpis.queueCurrent, kpis.failedWithoutRetryCurrent, kpis.activePrinters, kpis.activeStores);

        return Ok(new
        {
            generatedAtUtc = now,
            appliedFilters = new
            {
                window = NormalizeWindow(window),
                storeId = effectiveStoreId
            },
            kpis,
            thresholds,
            alerts,
            stores = storeRows
        });
    }

    [HttpGet("thresholds")]
    public async Task<IActionResult> GetThresholds(CancellationToken cancellationToken)
    {
        var thresholds = await GetThresholdsAsync(cancellationToken);
        return Ok(thresholds);
    }

    /// <summary>
    /// Reglas de severidad de 1-3 niveles por métrica (G5.3) — sustituye a dashboard-threshold-rules.json
    /// de PHP; la Api es ahora la fuente única de verdad, consumida también por el Worker.
    /// </summary>
    [HttpGet("threshold-rules")]
    public async Task<IActionResult> GetThresholdRules(CancellationToken cancellationToken)
    {
        var rules = await _ruleStore.LoadAsync(cancellationToken);
        return Ok(ToDto(rules));
    }

    [HttpPut("threshold-rules")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateThresholdRules([FromBody] ThresholdRuleSetRequest request, CancellationToken cancellationToken)
    {
        var rules = new ThresholdRuleSet(
            ToRules(request.Queue), ToRules(request.Failed), ToRules(request.MissingHost), ToRules(request.Conn));

        var errors = await _ruleStore.SaveAsync(rules, cancellationToken);
        if (errors.Count > 0)
            return BadRequest(new { errors });

        return Ok(ToDto(await _ruleStore.LoadAsync(cancellationToken)));
    }

    private static List<ThresholdRule> ToRules(List<ThresholdRuleRequest>? rules) =>
        (rules ?? []).Select(r => new ThresholdRule(r.Min, r.Severity ?? "warning")).ToList();

    private static object ToDto(ThresholdRuleSet rules) => new
    {
        queue = rules.Queue.Select(r => new { r.Min, r.Severity }),
        failed = rules.Failed.Select(r => new { r.Min, r.Severity }),
        missingHost = rules.MissingHost.Select(r => new { r.Min, r.Severity }),
        conn = rules.Conn.Select(r => new { r.Min, r.Severity })
    };

    [HttpPut("thresholds")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateThresholds([FromBody] UpdateDashboardThresholdsRequest request, CancellationToken cancellationToken)
    {
        if (request.WarningQueueMin < 0 || request.CriticalQueueMin < 0
            || request.WarningFailedWithoutRetryMin < 0 || request.CriticalFailedWithoutRetryMin < 0
            || request.MissingHostMin < 0
            || request.ConnWarningFailuresMin < 0 || request.ConnCriticalFailuresMin < 0)
        {
            return BadRequest(new { error = "Los umbrales no pueden ser negativos." });
        }

        if (request.WarningQueueMin >= request.CriticalQueueMin)
        {
            return BadRequest(new { error = "Warning de cola debe ser menor que Critical de cola." });
        }

        if (request.WarningFailedWithoutRetryMin >= request.CriticalFailedWithoutRetryMin)
        {
            return BadRequest(new { error = "Warning de fallos debe ser menor que Critical de fallos." });
        }

        if (request.ConnWarningFailuresMin >= request.ConnCriticalFailuresMin)
        {
            return BadRequest(new { error = "Warning de conectividad debe ser menor que Critical de conectividad." });
        }

        var thresholdsRow = await _dbContext.DashboardThresholds
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (thresholdsRow is null)
        {
            thresholdsRow = new DashboardThreshold { Id = 1 };
            await _dbContext.DashboardThresholds.AddAsync(thresholdsRow, cancellationToken);
        }

        thresholdsRow.WarningQueueMin = request.WarningQueueMin;
        thresholdsRow.CriticalQueueMin = request.CriticalQueueMin;
        thresholdsRow.QueueWarningSeverity = NormalizeSeverity(request.QueueWarningSeverity);
        thresholdsRow.QueueCriticalSeverity = NormalizeSeverity(request.QueueCriticalSeverity);
        thresholdsRow.WarningFailedWithoutRetryMin = request.WarningFailedWithoutRetryMin;
        thresholdsRow.CriticalFailedWithoutRetryMin = request.CriticalFailedWithoutRetryMin;
        thresholdsRow.FailedWarningSeverity = NormalizeSeverity(request.FailedWarningSeverity);
        thresholdsRow.FailedCriticalSeverity = NormalizeSeverity(request.FailedCriticalSeverity);
        thresholdsRow.MissingHostMin = request.MissingHostMin;
        thresholdsRow.MissingHostSeverity = NormalizeSeverity(request.MissingHostSeverity);
        thresholdsRow.ConnWarningFailuresMin = request.ConnWarningFailuresMin;
        thresholdsRow.ConnCriticalFailuresMin = request.ConnCriticalFailuresMin;
        thresholdsRow.ConnWarningSeverity = NormalizeSeverity(request.ConnWarningSeverity);
        thresholdsRow.ConnCriticalSeverity = NormalizeSeverity(request.ConnCriticalSeverity);
        thresholdsRow.UpdatedAtUtc = _timeProvider.GetUtcNow();

        await _dbContext.SaveChangesAsync(cancellationToken);

        var updated = await GetThresholdsAsync(cancellationToken);
        return Ok(updated);
    }

    /// <summary>
    /// "printed": primera vez que cada JobId entra en un estado de <see cref="DashboardPrintJobPredicates.PrintedStatuses"/>
    /// dentro de la ventana (no cada actualización posterior sobre el mismo estado impreso —
    /// KPI-P1-001). "failed": JobId distintos con al menos una señal de fallo en la ventana
    /// (entrada a ErrorFinal/RetryScheduled, o primera impresión con AttemptCount>1 — KPI-P1-002).
    /// Un mismo job nunca suma más de una vez a cada KPI dentro de la misma ventana.
    ///
    /// Perf (A-KPI-03, docs/auditoria-integral-2026-07-21.md): el MIN(OccurredAtUtc) agrupado por
    /// JobId se hacía sobre TODA la tabla de eventos (todas las tiendas, todo el histórico) y
    /// luego se resolvía StoreId/AttemptCount con un segundo roundtrip + IN-list de todos los
    /// JobId impresos. El Join contra <paramref name="jobsScope"/> (ya filtrado por tienda activa
    /// y, si aplica, storeId) empuja ese scope a la agregación y evita el segundo roundtrip.
    /// </summary>
    private async Task<(List<PrintedJobRow> Printed, List<FailedJobRow> Failed)> LoadPrintedAndFailedAsync(
        IQueryable<PrintJob> jobsScope, DateTimeOffset fromUtc, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var printed = await _dbContext.PrintJobEvents
            .Where(e => e.NewStatus != null && DashboardPrintJobPredicates.PrintedStatuses.Contains(e.NewStatus.Value))
            .Join(jobsScope, e => e.JobId, j => j.JobId,
                (e, j) => new { e.JobId, j.StoreId, j.AttemptCount, e.OccurredAtUtc })
            .GroupBy(x => new { x.JobId, x.StoreId, x.AttemptCount })
            .Select(g => new { g.Key.JobId, g.Key.StoreId, g.Key.AttemptCount, FirstPrintedAtUtc = g.Min(x => x.OccurredAtUtc) })
            .Where(x => x.FirstPrintedAtUtc >= fromUtc && x.FirstPrintedAtUtc <= now)
            .Select(x => new PrintedJobRow(x.JobId, x.StoreId, x.AttemptCount))
            .ToListAsync(cancellationToken);

        var errorOrRetry = await _dbContext.PrintJobEvents
            .Where(e => (e.NewStatus == PrintJobStatus.ErrorFinal || e.NewStatus == PrintJobStatus.RetryScheduled)
                        && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc <= now)
            .Join(jobsScope, e => e.JobId, j => j.JobId, (e, j) => new FailedJobRow(e.JobId, j.StoreId))
            .Distinct()
            .ToListAsync(cancellationToken);

        var failed = errorOrRetry
            .Concat(printed.Where(p => p.AttemptCount > 1).Select(p => new FailedJobRow(p.JobId, p.StoreId)))
            .GroupBy(x => x.JobId)
            .Select(g => g.First())
            .ToList();

        return (printed, failed);
    }

    private sealed record PrintedJobRow(Guid JobId, int StoreId, int AttemptCount);
    private sealed record FailedJobRow(Guid JobId, int StoreId);

    private async Task<(List<StoreDashboardRow> Rows, List<StoreAlert> Alerts)> BuildStoreRowsAsync(
        IQueryable<Domain.Entities.Store> stores,
        IQueryable<Domain.Entities.Printer> printers,
        IQueryable<Domain.Entities.PrintJob> jobsInWindow,
        IQueryable<Domain.Entities.PrintJob> allJobs,
        List<PrintedJobRow> printedRows,
        List<FailedJobRow> failedRows,
        DashboardThresholds thresholds,
        ThresholdRuleSet rules,
        CancellationToken cancellationToken)
    {
        var baseStores = await stores
            .Select(x => new { x.StoreId, x.Name })
            .OrderBy(x => x.StoreId)
            .ToListAsync(cancellationToken);

        var connectedPrinters = await printers
            .GroupBy(x => x.StoreId)
            .Select(x => new { StoreId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Count, cancellationToken);

        var printersConnStats = await LoadConnectivityStatsAsync(
            printers, thresholds.ConnWarningFailuresMin, thresholds.ConnCriticalFailuresMin, cancellationToken);

        var receivedStats = await jobsInWindow
            .GroupBy(x => x.StoreId)
            .Select(x => new { StoreId = x.Key, Received = x.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Received, cancellationToken);

        var printedStats = printedRows.GroupBy(p => p.StoreId).ToDictionary(g => g.Key, g => g.Count());
        var failedStats = failedRows.GroupBy(f => f.StoreId).ToDictionary(g => g.Key, g => g.Count());

        var queueCurrentStats = await allJobs
            .GroupBy(x => x.StoreId)
            .Select(x => new
            {
                StoreId = x.Key,
                QueuedCurrent = x.Count(j => DashboardPrintJobPredicates.QueueStatuses.Contains(j.Status))
            })
            .ToDictionaryAsync(x => x.StoreId, x => x.QueuedCurrent, cancellationToken);

        // Sin ventana (A-KPI-01): foto de estado actual, no filtrada por UpdatedAtUtc.
        var failedWindowStats = await allJobs
            .Where(DashboardPrintJobPredicates.FailedWithoutRetryCurrent)
            .GroupBy(x => x.StoreId)
            .Select(x => new
            {
                StoreId = x.Key,
                FailedWithoutRetryCurrent = x.Count()
            })
            .ToDictionaryAsync(x => x.StoreId, x => x.FailedWithoutRetryCurrent, cancellationToken);

        // Breakdown por impresora: evita que el fallback PHP tenga que pedir jobs crudos
        // (api/printjobs?limit=5000, truncado a 500 por la API) solo para calcular estos chips.
        var printerQueueCurrent = await allJobs
            .Where(j => DashboardPrintJobPredicates.QueueStatuses.Contains(j.Status) && j.PrinterId != null)
            .GroupBy(j => new { j.StoreId, PrinterId = j.PrinterId!.Value })
            .Select(g => new { g.Key.StoreId, g.Key.PrinterId, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var printerFailedWindow = await allJobs
            .Where(DashboardPrintJobPredicates.FailedWithoutRetryCurrent)
            .Where(j => j.PrinterId != null)
            .GroupBy(j => new { j.StoreId, PrinterId = j.PrinterId!.Value })
            .Select(g => new { g.Key.StoreId, g.Key.PrinterId, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // "recibidos por impresora": cohorte por CreatedAtUtc, igual que `received` — no eventos.
        var printerReceivedWindow = await jobsInWindow
            .Where(j => j.PrinterId != null)
            .GroupBy(j => new { j.StoreId, PrinterId = j.PrinterId!.Value })
            .Select(g => new { g.Key.StoreId, g.Key.PrinterId, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var unassignedQueueStats = await allJobs
            .Where(j => DashboardPrintJobPredicates.QueueStatuses.Contains(j.Status) && j.PrinterId == null)
            .GroupBy(j => j.StoreId)
            .Select(g => new { StoreId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Count, cancellationToken);

        var printerAcc = new Dictionary<(int StoreId, int PrinterId), (int Queue, int Failed, int Received)>();
        foreach (var r in printerQueueCurrent)
            printerAcc[(r.StoreId, r.PrinterId)] = printerAcc.TryGetValue((r.StoreId, r.PrinterId), out var q)
                ? (q.Queue + r.Count, q.Failed, q.Received) : (r.Count, 0, 0);
        foreach (var r in printerFailedWindow)
            printerAcc[(r.StoreId, r.PrinterId)] = printerAcc.TryGetValue((r.StoreId, r.PrinterId), out var f)
                ? (f.Queue, f.Failed + r.Count, f.Received) : (0, r.Count, 0);
        foreach (var r in printerReceivedWindow)
            printerAcc[(r.StoreId, r.PrinterId)] = printerAcc.TryGetValue((r.StoreId, r.PrinterId), out var t)
                ? (t.Queue, t.Failed, t.Received + r.Count) : (0, 0, r.Count);

        var printersByStore = printerAcc
            .GroupBy(kv => kv.Key.StoreId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PrinterDashboardRow>)g
                    .Select(kv => new PrinterDashboardRow(kv.Key.PrinterId, kv.Value.Queue, kv.Value.Failed, kv.Value.Received))
                    .OrderByDescending(p => p.QueueCurrent)
                    .ToList());

        var rows = new List<StoreDashboardRow>(baseStores.Count);
        var alertInputs = new List<StoreAlertInput>(baseStores.Count);
        foreach (var store in baseStores)
        {
            connectedPrinters.TryGetValue(store.StoreId, out var connected);
            printersConnStats.TryGetValue(store.StoreId, out var connStats);
            receivedStats.TryGetValue(store.StoreId, out var received);
            printedStats.TryGetValue(store.StoreId, out var printed);
            failedStats.TryGetValue(store.StoreId, out var failed);
            queueCurrentStats.TryGetValue(store.StoreId, out var queuedCurrentValue);
            failedWindowStats.TryGetValue(store.StoreId, out var failedWithoutRetryCurrentValue);
            unassignedQueueStats.TryGetValue(store.StoreId, out var unassignedQueueCurrent);
            printersByStore.TryGetValue(store.StoreId, out var storePrinters);

            var queuedCurrent = queuedCurrentValue;
            var failedWithoutRetryCurrent = failedWithoutRetryCurrentValue;

            var missingHost = connStats.MissingHost;
            var connMaxStreak = connStats.ConnMaxStreak;
            var connCrit = connStats.ConnCritical;
            var connWarn = connStats.ConnWarning;

            var (health, healthReason) = StoreHealthEvaluator.Compute(
                connected, queuedCurrent, failedWithoutRetryCurrent, missingHost,
                connMaxStreak, connCrit, connWarn, rules);

            rows.Add(new StoreDashboardRow(
                StoreId: store.StoreId,
                StoreName: store.Name,
                ConnectedPrinters: connected,
                Received: received,
                Printed: printed,
                Failed: failed,
                QueuedCurrent: queuedCurrent,
                FailedWithoutRetryCurrent: failedWithoutRetryCurrent,
                Health: health,
                HealthReason: healthReason,
                UnassignedQueueCurrent: unassignedQueueCurrent,
                Printers: storePrinters ?? []));

            alertInputs.Add(new StoreAlertInput(
                store.StoreId, store.Name, connected, queuedCurrent, failedWithoutRetryCurrent,
                missingHost, connMaxStreak, connCrit, connWarn));
        }

        var alerts = StoreHealthEvaluator.BuildAlerts(alertInputs, rules).ToList();
        return (rows, alerts);
    }

    /// <summary>
    /// Clasifica conectividad por impresora reutilizando <see cref="PrinterConnectivityState"/> (misma
    /// lógica que PrinterConnectivityMonitorService), y agrega por tienda (G5.3): missingHost por
    /// estado "sin host" real (no solo campo Host vacío), connCritical/connWarning con el mismo
    /// criterio OR (severidad de conectividad || streak &gt;= umbral legacy) que usaba PHP.
    /// </summary>
    private static async Task<Dictionary<int, ConnectivityStats>> LoadConnectivityStatsAsync(
        IQueryable<Domain.Entities.Printer> printers, int connWarnMin, int connCritMin, CancellationToken cancellationToken)
    {
        var rows = await printers
            .Select(p => new
            {
                p.StoreId, p.IsActive, p.LastConnectionOk, p.LastConnectionCheckAtUtc,
                p.LastConnectionError, p.ConnectionFailuresStreak
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(p => p.StoreId)
            .ToDictionary(g => g.Key, g =>
            {
                int missingHost = 0, warn = 0, crit = 0, maxStreak = 0;
                foreach (var p in g)
                {
                    var descriptor = PrinterConnectivityState.FromSnapshot(
                        p.IsActive, p.LastConnectionOk, p.LastConnectionCheckAtUtc, p.LastConnectionError, p.ConnectionFailuresStreak);

                    if (descriptor.ConnectivityStatus == PrinterConnectivityState.StatusNoHost)
                        missingHost++;

                    maxStreak = Math.Max(maxStreak, p.ConnectionFailuresStreak);

                    if (descriptor.ConnectivitySeverity == PrinterConnectivityState.SeverityCritical || p.ConnectionFailuresStreak >= connCritMin)
                        crit++;
                    else if ((descriptor.ConnectivitySeverity == PrinterConnectivityState.SeverityWarning && descriptor.ConnectivityStatus != PrinterConnectivityState.StatusNoHost)
                             || p.ConnectionFailuresStreak >= connWarnMin)
                        warn++;
                }

                return new ConnectivityStats(missingHost, warn, crit, maxStreak);
            });
    }

    private readonly record struct ConnectivityStats(int MissingHost, int ConnWarning, int ConnCritical, int ConnMaxStreak);

    private async Task<DashboardThresholds> GetThresholdsAsync(CancellationToken cancellationToken)
    {
        var row = await _dbContext.DashboardThresholds
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (row is not null)
            return MapThresholdRow(row);

        var defaults = new DashboardThresholds(
            DefaultWarningQueueMin,
            DefaultCriticalQueueMin,
            "warning",
            "critical",
            DefaultWarningFailedWithoutRetryMin,
            DefaultCriticalFailedWithoutRetryMin,
            "warning",
            "critical",
            DefaultMissingHostMin,
            "warning",
            DefaultConnWarningFailuresMin,
            DefaultConnCriticalFailuresMin,
            "warning",
            "critical");

        var defaultRow = new DashboardThreshold
        {
            Id = 1,
            WarningQueueMin = defaults.WarningQueueMin,
            CriticalQueueMin = defaults.CriticalQueueMin,
            QueueWarningSeverity = defaults.QueueWarningSeverity,
            QueueCriticalSeverity = defaults.QueueCriticalSeverity,
            WarningFailedWithoutRetryMin = defaults.WarningFailedWithoutRetryMin,
            CriticalFailedWithoutRetryMin = defaults.CriticalFailedWithoutRetryMin,
            FailedWarningSeverity = defaults.FailedWarningSeverity,
            FailedCriticalSeverity = defaults.FailedCriticalSeverity,
            MissingHostMin = defaults.MissingHostMin,
            MissingHostSeverity = defaults.MissingHostSeverity,
            ConnWarningFailuresMin = defaults.ConnWarningFailuresMin,
            ConnCriticalFailuresMin = defaults.ConnCriticalFailuresMin,
            ConnWarningSeverity = defaults.ConnWarningSeverity,
            ConnCriticalSeverity = defaults.ConnCriticalSeverity,
            UpdatedAtUtc = _timeProvider.GetUtcNow()
        };

        try
        {
            await _dbContext.DashboardThresholds.AddAsync(defaultRow, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent request inserted the singleton first; reload what it wrote.
            _dbContext.ChangeTracker.Clear();
            var concurrent = await _dbContext.DashboardThresholds
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
            if (concurrent is not null)
                return MapThresholdRow(concurrent);
            throw;
        }

        return defaults;
    }

    private static DashboardThresholds MapThresholdRow(DashboardThreshold row) =>
        new(row.WarningQueueMin,
            row.CriticalQueueMin,
            row.QueueWarningSeverity,
            row.QueueCriticalSeverity,
            row.WarningFailedWithoutRetryMin,
            row.CriticalFailedWithoutRetryMin,
            row.FailedWarningSeverity,
            row.FailedCriticalSeverity,
            row.MissingHostMin,
            row.MissingHostSeverity,
            row.ConnWarningFailuresMin,
            row.ConnCriticalFailuresMin,
            row.ConnWarningSeverity,
            row.ConnCriticalSeverity);

    private static string NormalizeSeverity(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        return v is "info" or "warning" or "critical" ? v : "warning";
    }

    private DateTimeOffset ResolveWindowStartUtc(string? window, DateTimeOffset now)
    {
        return NormalizeWindow(window) switch
        {
            "7d" => now.AddDays(-7),
            "30d" => now.AddDays(-30),
            // "today" en la timezone de negocio (Dashboard:BusinessTimeZone), no en la del servidor.
            _ => BusinessTimeZoneClock.TodayStartUtc(now, _businessTimeZone)
        };
    }

    private static string NormalizeWindow(string? window)
    {
        var normalized = (window ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "7d" or "30d" ? normalized : "today";
    }

    private bool IsAdmin() => User.IsInRole("Admin");

    private int? GetCurrentUserStoreId()
    {
        var claimValue = User.FindFirstValue("StoreId");
        return int.TryParse(claimValue, out var parsed) ? parsed : null;
    }

    private sealed record StoreDashboardRow(
        int StoreId,
        string StoreName,
        int ConnectedPrinters,
        int Received,
        int Printed,
        int Failed,
        int QueuedCurrent,
        int FailedWithoutRetryCurrent,
        string Health,
        string HealthReason,
        int UnassignedQueueCurrent,
        IReadOnlyList<PrinterDashboardRow> Printers);

    private sealed record PrinterDashboardRow(
        int PrinterId,
        int QueueCurrent,
        int FailedWindow,
        int ReceivedWindow);

    private sealed record DashboardThresholds(
        int WarningQueueMin,
        int CriticalQueueMin,
        string QueueWarningSeverity,
        string QueueCriticalSeverity,
        int WarningFailedWithoutRetryMin,
        int CriticalFailedWithoutRetryMin,
        string FailedWarningSeverity,
        string FailedCriticalSeverity,
        int MissingHostMin,
        string MissingHostSeverity,
        int ConnWarningFailuresMin,
        int ConnCriticalFailuresMin,
        string ConnWarningSeverity,
        string ConnCriticalSeverity);
}

public record UpdateDashboardThresholdsRequest(
    int WarningQueueMin,
    int CriticalQueueMin,
    int WarningFailedWithoutRetryMin,
    int CriticalFailedWithoutRetryMin,
    string? QueueWarningSeverity,
    string? QueueCriticalSeverity,
    string? FailedWarningSeverity,
    string? FailedCriticalSeverity,
    int MissingHostMin,
    string? MissingHostSeverity,
    int ConnWarningFailuresMin,
    int ConnCriticalFailuresMin,
    string? ConnWarningSeverity,
    string? ConnCriticalSeverity);

public record ThresholdRuleRequest(int Min, string? Severity);

public record ThresholdRuleSetRequest(
    List<ThresholdRuleRequest>? Queue,
    List<ThresholdRuleRequest>? Failed,
    List<ThresholdRuleRequest>? MissingHost,
    List<ThresholdRuleRequest>? Conn);
