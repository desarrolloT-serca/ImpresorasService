using System.Security.Claims;
using ImpresorasService.Domain;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Controllers;

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

    private static readonly PrintJobStatus[] FailedWithoutRetryStatuses =
    [
        PrintJobStatus.ErrorFinal,
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
        var stores = _dbContext.Stores.AsNoTracking().Where(x => x.IsActive);
        var printers = _dbContext.Printers.AsNoTracking().Where(x => x.IsActive);

        if (effectiveStoreId.HasValue)
        {
            jobs = jobs.Where(x => x.StoreId == effectiveStoreId.Value);
            stores = stores.Where(x => x.StoreId == effectiveStoreId.Value);
            printers = printers.Where(x => x.StoreId == effectiveStoreId.Value);
        }

        var jobsInWindow = jobs.Where(x => x.CreatedAtUtc >= fromUtc);

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
            failedWithoutRetryCurrent = await jobsInWindow.CountAsync(
                x => FailedWithoutRetryStatuses.Contains(x.Status)
                     || (!PrintedStatuses.Contains(x.Status) && x.AttemptCount > 1),
                cancellationToken),
            activePrinters = await printers.CountAsync(cancellationToken),
            activeStores = await stores.CountAsync(cancellationToken)
        };

        var storeRows = await BuildStoreRowsAsync(stores, printers, jobsInWindow, jobs, thresholds, cancellationToken);
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

        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(cancellationToken);

        await using var upsert = conn.CreateCommand();
        upsert.CommandText = @"
            INSERT INTO DashboardThresholds
                (Id,
                 WarningQueueMin, CriticalQueueMin, QueueWarningSeverity, QueueCriticalSeverity,
                 WarningFailedWithoutRetryMin, CriticalFailedWithoutRetryMin, FailedWarningSeverity, FailedCriticalSeverity,
                 MissingHostMin, MissingHostSeverity,
                 ConnWarningFailuresMin, ConnCriticalFailuresMin, ConnWarningSeverity, ConnCriticalSeverity,
                 UpdatedAtUtc)
            VALUES
                (1,
                 @warningQueueMin, @criticalQueueMin, @queueWarnSev, @queueCritSev,
                 @warningFailedMin, @criticalFailedMin, @failedWarnSev, @failedCritSev,
                 @missingHostMin, @missingHostSev,
                 @connWarnMin, @connCritMin, @connWarnSev, @connCritSev,
                 @updatedAtUtc)
            ON CONFLICT(Id) DO UPDATE SET
                WarningQueueMin = excluded.WarningQueueMin,
                CriticalQueueMin = excluded.CriticalQueueMin,
                QueueWarningSeverity = excluded.QueueWarningSeverity,
                QueueCriticalSeverity = excluded.QueueCriticalSeverity,
                WarningFailedWithoutRetryMin = excluded.WarningFailedWithoutRetryMin,
                CriticalFailedWithoutRetryMin = excluded.CriticalFailedWithoutRetryMin,
                FailedWarningSeverity = excluded.FailedWarningSeverity,
                FailedCriticalSeverity = excluded.FailedCriticalSeverity,
                MissingHostMin = excluded.MissingHostMin,
                MissingHostSeverity = excluded.MissingHostSeverity,
                ConnWarningFailuresMin = excluded.ConnWarningFailuresMin,
                ConnCriticalFailuresMin = excluded.ConnCriticalFailuresMin,
                ConnWarningSeverity = excluded.ConnWarningSeverity,
                ConnCriticalSeverity = excluded.ConnCriticalSeverity,
                UpdatedAtUtc = excluded.UpdatedAtUtc;";
        AddParameter(upsert, "@warningQueueMin", request.WarningQueueMin);
        AddParameter(upsert, "@criticalQueueMin", request.CriticalQueueMin);
        AddParameter(upsert, "@queueWarnSev", NormalizeSeverity(request.QueueWarningSeverity));
        AddParameter(upsert, "@queueCritSev", NormalizeSeverity(request.QueueCriticalSeverity));
        AddParameter(upsert, "@warningFailedMin", request.WarningFailedWithoutRetryMin);
        AddParameter(upsert, "@criticalFailedMin", request.CriticalFailedWithoutRetryMin);
        AddParameter(upsert, "@failedWarnSev", NormalizeSeverity(request.FailedWarningSeverity));
        AddParameter(upsert, "@failedCritSev", NormalizeSeverity(request.FailedCriticalSeverity));
        AddParameter(upsert, "@missingHostMin", request.MissingHostMin);
        AddParameter(upsert, "@missingHostSev", NormalizeSeverity(request.MissingHostSeverity));
        AddParameter(upsert, "@connWarnMin", request.ConnWarningFailuresMin);
        AddParameter(upsert, "@connCritMin", request.ConnCriticalFailuresMin);
        AddParameter(upsert, "@connWarnSev", NormalizeSeverity(request.ConnWarningSeverity));
        AddParameter(upsert, "@connCritSev", NormalizeSeverity(request.ConnCriticalSeverity));
        AddParameter(upsert, "@updatedAtUtc", DateTimeOffset.UtcNow.ToString("o"));
        await upsert.ExecuteNonQueryAsync(cancellationToken);

        var updated = await GetThresholdsAsync(cancellationToken);
        return Ok(updated);
    }

    private async Task<List<StoreDashboardRow>> BuildStoreRowsAsync(
        IQueryable<Domain.Entities.Store> stores,
        IQueryable<Domain.Entities.Printer> printers,
        IQueryable<Domain.Entities.PrintJob> jobsInWindow,
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

        var failedWindowStats = await jobsInWindow
            .GroupBy(x => x.StoreId)
            .Select(x => new
            {
                StoreId = x.Key,
                FailedWithoutRetryCurrent = x.Count(j =>
                    FailedWithoutRetryStatuses.Contains(j.Status)
                    || (!PrintedStatuses.Contains(j.Status) && j.AttemptCount > 1))
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
        DashboardThresholds thresholds)
    {
        // Prioridad: conectividad crítica
        if (connCrit > 0)
            return (ToHealth(thresholds.ConnCriticalSeverity), "Impresora(s) sin conexion (conectividad)");

        if (connectedPrinters == 0 && queuedCurrent > 0)
            return ("critical", "Hay cola pero no hay impresoras activas");

        if (failedWithoutRetryCurrent >= thresholds.CriticalFailedWithoutRetryMin || queuedCurrent >= thresholds.CriticalQueueMin)
        {
            if (failedWithoutRetryCurrent >= thresholds.CriticalFailedWithoutRetryMin)
                return (ToHealth(thresholds.FailedCriticalSeverity), $"Acumula {thresholds.CriticalFailedWithoutRetryMin} o mas fallos sin reenviar");
            return (ToHealth(thresholds.QueueCriticalSeverity), $"Cola actual mayor o igual a {thresholds.CriticalQueueMin} trabajos");
        }
        if (connectedPrinters == 0)
            return ("warning", "Sin impresoras activas en la tienda");

        if (connWarn > 0)
            return (ToHealth(thresholds.ConnWarningSeverity), "Impresora(s) con fallos de conexion (conectividad)");
        if (missingHost >= thresholds.MissingHostMin && missingHost > 0)
            return (ToHealth(thresholds.MissingHostSeverity), "Impresora(s) sin host configurado");

        if (failedWithoutRetryCurrent >= thresholds.WarningFailedWithoutRetryMin || queuedCurrent >= thresholds.WarningQueueMin)
        {
            if (failedWithoutRetryCurrent >= thresholds.WarningFailedWithoutRetryMin)
                return (ToHealth(thresholds.FailedWarningSeverity), "Tiene fallos recientes sin reenviar");
            return (ToHealth(thresholds.QueueWarningSeverity), $"Cola actual entre {thresholds.WarningQueueMin} y {thresholds.CriticalQueueMin - 1} trabajos");
        }
        return ("healthy", "Operacion dentro de umbrales");
    }

    private async Task<DashboardThresholds> GetThresholdsAsync(CancellationToken cancellationToken)
    {
        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                WarningQueueMin, CriticalQueueMin, QueueWarningSeverity, QueueCriticalSeverity,
                WarningFailedWithoutRetryMin, CriticalFailedWithoutRetryMin, FailedWarningSeverity, FailedCriticalSeverity,
                MissingHostMin, MissingHostSeverity,
                ConnWarningFailuresMin, ConnCriticalFailuresMin, ConnWarningSeverity, ConnCriticalSeverity
            FROM DashboardThresholds
            WHERE Id = 1
            LIMIT 1;";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new DashboardThresholds(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetInt32(8),
                reader.GetString(9),
                reader.GetInt32(10),
                reader.GetInt32(11),
                reader.GetString(12),
                reader.GetString(13));
        }

        return new DashboardThresholds(
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
    }

    private static string NormalizeSeverity(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        return v is "info" or "warning" or "critical" ? v : "warning";
    }

    private static string ToHealth(string severity)
    {
        // El dashboard actual solo distingue healthy/warning/critical.
        // "info" no debe elevar el estado, pero sí puede mostrarse en reason.
        var s = NormalizeSeverity(severity);
        return s == "critical" ? "critical" : (s == "warning" ? "warning" : "healthy");
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static DateTimeOffset ResolveWindowStartUtc(string? window)
    {
        var now = DateTimeOffset.UtcNow;
        return NormalizeWindow(window) switch
        {
            "7d" => now.AddDays(-7),
            "30d" => now.AddDays(-30),
            _ => now.Date
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
