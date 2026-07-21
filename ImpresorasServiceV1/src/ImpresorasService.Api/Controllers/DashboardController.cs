using System.Security.Claims;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
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
    private const int DefaultWarningQueueMin = 10;
    private const int DefaultCriticalQueueMin = 30;
    private const int DefaultWarningFailedWithoutRetryMin = 1;
    private const int DefaultCriticalFailedWithoutRetryMin = 5;
    private const int DefaultMissingHostMin = 1;
    private const int DefaultConnWarningFailuresMin = 2;
    private const int DefaultConnCriticalFailuresMin = 3;

    public DashboardController(ImpresorasDbContext dbContext, TimeProvider timeProvider, IConfiguration configuration, ILogger<DashboardController> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
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
        var jobs = _dbContext.PrintJobs.AsNoTracking()
            .Where(x => _dbContext.Stores.Any(s => s.StoreId == x.StoreId && s.IsActive == activeOnly));

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

        var kpis = new
        {
            received = await jobsInWindow.CountAsync(cancellationToken),
            printed = printedRows.Count,
            failed = failedRows.Count,
            queueCurrent = await jobs.CountAsync(x => DashboardPrintJobPredicates.QueueStatuses.Contains(x.Status), cancellationToken),
            failedWithoutRetryCurrent = await jobs.CountAsync(DashboardPrintJobPredicates.FailedWithoutRetryCurrent, cancellationToken),
            activePrinters = await printers.CountAsync(cancellationToken),
            activeStores = await stores.CountAsync(cancellationToken)
        };

        var storeRows = await BuildStoreRowsAsync(stores, printers, jobsInWindow, jobs, printedRows, failedRows, thresholds, cancellationToken);
        var alerts = storeRows
            .Where(x => x.Health != "healthy")
            .OrderByDescending(x => x.FailedWithoutRetryCurrent)
            .ThenByDescending(x => x.QueuedCurrent)
            .Select(x => new
            {
                x.StoreId,
                x.StoreName,
                x.Health,
                x.HealthReason,
                x.QueuedCurrent,
                x.FailedWithoutRetryCurrent
            })
            .ToList();

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

    private async Task<List<StoreDashboardRow>> BuildStoreRowsAsync(
        IQueryable<Domain.Entities.Store> stores,
        IQueryable<Domain.Entities.Printer> printers,
        IQueryable<Domain.Entities.PrintJob> jobsInWindow,
        IQueryable<Domain.Entities.PrintJob> allJobs,
        List<PrintedJobRow> printedRows,
        List<FailedJobRow> failedRows,
        DashboardThresholds thresholds,
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

        var printersConnStats = await printers
            .GroupBy(x => x.StoreId)
            .Select(x => new
            {
                StoreId = x.Key,
                MissingHost = x.Count(p => (p.Host == null || p.Host == "") && !EF.Functions.Like(p.SpoolQueue, "\\\\%")),
                ConnWarn = x.Count(p => p.ConnectionFailuresStreak >= thresholds.ConnWarningFailuresMin),
                ConnCrit = x.Count(p => p.ConnectionFailuresStreak >= thresholds.ConnCriticalFailuresMin),
            })
            .ToDictionaryAsync(x => x.StoreId, x => new { x.MissingHost, x.ConnWarn, x.ConnCrit }, cancellationToken);

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

            var missingHost = connStats?.MissingHost ?? 0;
            var connWarn = connStats?.ConnWarn ?? 0;
            var connCrit = connStats?.ConnCrit ?? 0;
            var (health, healthReason) = ComputeHealth(connected, queuedCurrent, failedWithoutRetryCurrent, missingHost, connWarn, connCrit, thresholds);

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
        }

        return rows;
    }

    private static (string health, string reason) ComputeHealth(
        int connectedPrinters,
        int queuedCurrent,
        int failedWithoutRetryCurrent,
        int missingHost,
        int connWarn,
        int connCrit,
        DashboardThresholds t)
        => StoreHealthEvaluator.Compute(
            connectedPrinters, queuedCurrent, failedWithoutRetryCurrent,
            missingHost, connWarn, connCrit,
            t.WarningQueueMin, t.CriticalQueueMin,
            t.WarningFailedWithoutRetryMin, t.CriticalFailedWithoutRetryMin,
            t.MissingHostMin, t.ConnWarningFailuresMin, t.ConnCriticalFailuresMin,
            t.ConnCriticalSeverity, t.FailedCriticalSeverity, t.QueueCriticalSeverity,
            t.ConnWarningSeverity, t.MissingHostSeverity, t.FailedWarningSeverity, t.QueueWarningSeverity);

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
