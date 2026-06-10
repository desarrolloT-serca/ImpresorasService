using System.Diagnostics;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Models;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Adapters;

public class SqlTestJobSourceAdapter : IJobSourceAdapter
{
    private readonly SourceOptions _options;
    private readonly ImpresorasDbContext _dbContext;
    private readonly ILogger<SqlTestJobSourceAdapter> _logger;
    private readonly string _workerId;

    public SqlTestJobSourceAdapter(
        IOptions<SourceOptions> options,
        ImpresorasDbContext dbContext,
        ILogger<SqlTestJobSourceAdapter> logger)
    {
        _options = options.Value;
        _dbContext = dbContext;
        _logger = logger;
        _workerId = $"{Environment.MachineName}-{Process.GetCurrentProcess().Id}";
    }

    public async Task<IReadOnlyList<IncomingPrintJob>> FetchPendingJobsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.Mode, "SqlTest", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<IncomingPrintJob>();

        if (batchSize <= 0)
            return Array.Empty<IncomingPrintJob>();

        var claimToken = Guid.NewGuid().ToString("N");
        var leaseSeconds = Math.Max(15, _options.SqlTestLeaseSeconds);
        var leaseEnd = DateTimeOffset.UtcNow.AddSeconds(leaseSeconds);
        var now = DateTimeOffset.UtcNow;

        // Un solo UPDATE atómico: evita que dos workers lean el mismo subconjunto antes de marcar.
        var affected = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $@"
UPDATE SourcePrintJobs
SET ClaimedBy = {_workerId},
    ClaimedUntilUtc = {leaseEnd},
    ClaimToken = {claimToken}
WHERE Id IN (
  SELECT Id FROM SourcePrintJobs
  WHERE IsProcessed = 0 AND (ClaimedUntilUtc IS NULL OR ClaimedUntilUtc <= {now})
  ORDER BY Id
  LIMIT {batchSize}
)",
            cancellationToken);

        if (affected == 0)
            return Array.Empty<IncomingPrintJob>();

        var sourceRows = await _dbContext.SourcePrintJobs
            .AsNoTracking()
            .Where(x => x.ClaimToken == claimToken)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var jobs = new List<IncomingPrintJob>(sourceRows.Count);
        foreach (SourcePrintJobRecord row in sourceRows)
        {
            jobs.Add(new IncomingPrintJob(
                SourceJobId: row.Id,
                SourceSystem: row.SourceSystem,
                ExternalJobId: row.ExternalJobId,
                StoreId: row.StoreId,
                DocumentType: row.DocumentType,
                Channel: string.IsNullOrWhiteSpace(row.Channel) ? "DEFAULT" : row.Channel,
                PdfBlob: row.PdfBlob,
                CreatedAtUtc: row.CreatedAtUtc));
        }

        _logger.LogInformation("SqlTest adapter reclamó {Count} trabajos pendientes (lote atómico).", jobs.Count);
        return jobs;
    }

    public async Task MarkJobsProcessedAsync(
        IReadOnlyList<long> sourceJobIds,
        CancellationToken cancellationToken)
    {
        if (sourceJobIds is null || sourceJobIds.Count == 0)
            return;

        var idSet = sourceJobIds.ToHashSet();
        var rows = await _dbContext.SourcePrintJobs
            .Where(x => idSet.Contains(x.Id))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (row.ClaimedBy is not null
                && !string.Equals(row.ClaimedBy, _workerId, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "SqlTest ack omitido Id={Id}: ClaimedBy distinto (Expected={Expected}, Actual={Actual}).",
                    row.Id,
                    _workerId,
                    row.ClaimedBy);
                continue;
            }

            row.IsProcessed = true;
            row.ClaimedBy = null;
            row.ClaimedUntilUtc = null;
            row.ClaimToken = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RenewJobLeasesAsync(IReadOnlyList<long> sourceJobIds, CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.Mode, "SqlTest", StringComparison.OrdinalIgnoreCase))
            return;

        if (sourceJobIds is null || sourceJobIds.Count == 0)
            return;

        var leaseSeconds = Math.Max(15, _options.SqlTestLeaseSeconds);
        var newExpiry = DateTimeOffset.UtcNow.AddSeconds(leaseSeconds);
        var ids = sourceJobIds.Distinct().ToArray();

        await _dbContext.SourcePrintJobs
            .Where(x => ids.Contains(x.Id) && x.ClaimedBy == _workerId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.ClaimedUntilUtc, newExpiry),
                cancellationToken);
    }
}
