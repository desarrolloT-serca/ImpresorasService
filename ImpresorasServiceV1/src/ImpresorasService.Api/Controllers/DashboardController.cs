using System.Linq.Expressions;
using System.Security.Claims;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Controllers;

public static class DashboardPrintJobPredicates
{
    public static readonly Expression<Func<PrintJob, bool>> FailedWithoutRetryCurrent =
        x => x.Status == PrintJobStatus.ErrorFinal
             || ((x.Status == PrintJobStatus.Pending
                  || x.Status == PrintJobStatus.Routed
                  || x.Status == PrintJobStatus.Printing
                  || x.Status == PrintJobStatus.Cancelled
                  || x.Status == PrintJobStatus.PrinterBlocked)
                 && x.AttemptCount > 1);
}

[ApiController]
[Authorize(Policy = "EmployeeOrAbove")]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private static readonly PrintJobStatus[] PrintedStatuses =
    [
        PrintJobStatus.SpoolAccepted,
        PrintJobStatus.PrintedConfirmed,
        PrintJobStatus.PrintedUnknown
    ];

    private static readonly PrintJobStatus[] QueueStatuses =
    [
        PrintJobStatus.Pending,
        PrintJobStatus.Routed,
        PrintJobStatus.Printing,
        PrintJobStatus.RetryScheduled
    ];

    private readonly ImpresorasDbContext _dbContext;
    private const int DefaultWarningQueueMin = 10;
    private const int DefaultCriticalQueueMin = 30;
    private const int DefaultWarningFailedWithoutRetryMin = 1;
    private const int DefaultCriticalFailedWithoutRetryMin = 5;
    private const int DefaultMissingHostMin = 1;
    private const int DefaultConnWarningFailuresMin = 2;
    private const int DefaultConnCriticalFailuresMin = 3;

    public DashboardController(ImpresorasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(
        [FromQuery] string? window,
        [FromQuery] int? storeId,
        CancellationToken cancellationToken)
    {
        var thresholds = await GetThresholdsAsync(cancellationToken);
        var effectiveStoreId = IsAdmin() ? storeId : GetCurrentUserStoreId();
        var fromUtc = ResolveWindowStartUtc(window);

        var jobs = _dbContext.PrintJobs.AsNoTracking();
        var activeOnly = true;
        var stores = _dbContext.Stores.AsNoTracking().Where(x => x.IsActive == activeOnly);
        var printers = _dbContext.Printers.AsNoTracking().Where(x => x.IsActive == activeOnly);

        if (effectiveStoreId.HasValue)
        {
            jobs = jobs.Where(x => x.StoreId == effectiveStoreId.Value);
            stores = stores.Where(x => x.StoreId == effectiveStoreId.Value);
            printers = printers.Where(x => x.StoreId == effectiveStoreId.Value);
        }

        var jobsInWindow = jobs.Where(x => x.CreatedAtUtc >= fromUtc);
        // failedWithoutRetryCurrent usa UpdatedAtUtc para reflejar trabajos que
        // entraron en error durante la ventana, no solo los creados en ella.
        var jobsUpdatedInWindow = jobs.Where(x => x.UpdatedAtUtc >= fromUtc);

        var kpis = new
        {
            received = await jobsInWindow.CountAsync(cancellationToken),
            printed = await jobsInWindow.CountAsync(x => PrintedStatuses.Contains(x.Status), cancellationToken),
            failed = await jobsInWindow.CountAsync(
                x => x.Status == PrintJobStatus.ErrorFinal
                     || x.Status == PrintJobStatus.RetryScheduled
                     || (PrintedStatuses.Contains(x.Status) && x.AttemptCount > 1),
                cancellationToken),
            queueCurrent = await jobs.CountAsync(x => QueueStatuses.Contains(x.Status), cancellationToken),
            failedWithoutRetryCurrent = await jobsUpdatedInWindow.CountAsync(DashboardPrintJobPredicates.FailedWithoutRetryCurrent, cancellationToken),
            activePrinters = await printers.CountAsync(cancellationToken),
            activeStores = await stores.CountAsync(cancellationToken)
        };

        var storeRows = await BuildStoreRowsAsync(stores, printers, jobsInWindow, jobsUpdatedInWindow, jobs, thresholds, cancellationToken);
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
            generatedAtUtc = DateTimeOffset.UtcNow,
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
        thresholdsRow.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var updated = await GetThresholdsAsync(cancellationToken);
        return Ok(updated);
    }

    private async Task<List<StoreDashboardRow>> BuildStoreRowsAsync(
        IQueryable<Domain.Entities.Store> stores,
        IQueryable<Domain.Entities.Printer> printers,
        IQueryable<Domain.Entities.PrintJob> jobsInWindow,
        IQueryable<Domain.Entities.PrintJob> jobsUpdatedInWindow,
        IQueryable<Domain.Entities.PrintJob> allJobs,
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

        var jobsWindowStats = await jobsInWindow
            .GroupBy(x => x.StoreId)
            .Select(x => new
            {
                StoreId = x.Key,
                Received = x.Count(),
                Printed = x.Count(j => PrintedStatuses.Contains(j.Status)),
                Failed = x.Count(j =>
                    j.Status == PrintJobStatus.ErrorFinal
                    || j.Status == PrintJobStatus.RetryScheduled
                    || (PrintedStatuses.Contains(j.Status) && j.AttemptCount > 1))
            })
            .ToDictionaryAsync(x => x.StoreId, x => new { x.Received, x.Printed, x.Failed }, cancellationToken);

        var queueCurrentStats = await allJobs
            .GroupBy(x => x.StoreId)
            .Select(x => new
            {
                StoreId = x.Key,
                QueuedCurrent = x.Count(j => QueueStatuses.Contains(j.Status))
            })
            .ToDictionaryAsync(x => x.StoreId, x => x.QueuedCurrent, cancellationToken);

        var failedWindowStats = await jobsUpdatedInWindow
            .Where(DashboardPrintJobPredicates.FailedWithoutRetryCurrent)
            .GroupBy(x => x.StoreId)
            .Select(x => new
            {
                StoreId = x.Key,
                FailedWithoutRetryCurrent = x.Count()
            })
            .ToDictionaryAsync(x => x.StoreId, x => x.FailedWithoutRetryCurrent, cancellationToken);

        var rows = new List<StoreDashboardRow>(baseStores.Count);
        foreach (var store in baseStores)
        {
            connectedPrinters.TryGetValue(store.StoreId, out var connected);
            printersConnStats.TryGetValue(store.StoreId, out var connStats);
            jobsWindowStats.TryGetValue(store.StoreId, out var windowStats);
            queueCurrentStats.TryGetValue(store.StoreId, out var queuedCurrentValue);
            failedWindowStats.TryGetValue(store.StoreId, out var failedWithoutRetryCurrentValue);

            var received = windowStats?.Received ?? 0;
            var printed = windowStats?.Printed ?? 0;
            var failed = windowStats?.Failed ?? 0;
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
                HealthReason: healthReason));
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
            UpdatedAtUtc = DateTimeOffset.UtcNow
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

    private static DateTimeOffset ResolveWindowStartUtc(string? window)
    {
        var now = DateTimeOffset.UtcNow;
        // ponytail: DateTime.Today es medianoche local; igual que el frontend PHP que usa config('app.timezone').
        // now.Date es medianoche UTC — difiere hasta 2h en CEST, haciendo que C# devuelva received=0
        // para trabajos creados entre 00:00-02:00 hora local, vaciando el dashboard.
        var todayLocal = new DateTimeOffset(DateTime.Today, TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));
        return NormalizeWindow(window) switch
        {
            "7d" => now.AddDays(-7),
            "30d" => now.AddDays(-30),
            _ => todayLocal
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
        string HealthReason);

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
