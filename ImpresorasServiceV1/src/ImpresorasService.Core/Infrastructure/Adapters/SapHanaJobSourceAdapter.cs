using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Models;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace ImpresorasService.Infrastructure.Adapters;

public class SapHanaJobSourceAdapter : IJobSourceAdapter
{
    private readonly SourceOptions _options;
    private readonly SapHanaOptions _hanaOptions;
    private readonly ImpresorasDbContext _dbContext;
    private readonly ILogger<SapHanaJobSourceAdapter> _logger;
    private readonly string _workerId;
    private readonly string _sourceSystem;

    public SapHanaJobSourceAdapter(
        IOptions<SourceOptions> options,
        IOptions<SapHanaOptions> hanaOptions,
        ImpresorasDbContext dbContext,
        ILogger<SapHanaJobSourceAdapter> logger)
    {
        _options = options.Value;
        _hanaOptions = hanaOptions.Value;
        _dbContext = dbContext;
        _logger = logger;
        _workerId = $"{Environment.MachineName}-{Process.GetCurrentProcess().Id}";
        _sourceSystem = string.IsNullOrWhiteSpace(_hanaOptions.SourceSystem) ? "SAP-HANA" : _hanaOptions.SourceSystem.Trim();
    }

    public async Task<IReadOnlyList<IncomingPrintJob>> FetchPendingJobsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.Mode, "SapHana", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<IncomingPrintJob>();

        if (batchSize <= 0)
            return Array.Empty<IncomingPrintJob>();

        var claimToken = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var leaseSeconds = Math.Max(15, _hanaOptions.LeaseSeconds);
        var leaseEnd = now.AddSeconds(leaseSeconds);

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var preCandidates = await _dbContext.SourcePrintJobs
            .Where(x => !x.IsProcessed)
            .OrderBy(x => x.Id)
            .Take(batchSize * 5)
            .ToListAsync(cancellationToken);

        var candidateIds = preCandidates
            .Where(x => x.ClaimedUntilUtc == null || x.ClaimedUntilUtc <= now)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToList();

        if (candidateIds.Count == 0)
        {
            await tx.CommitAsync(cancellationToken);
            return Array.Empty<IncomingPrintJob>();
        }

        var claimedRows = (await _dbContext.SourcePrintJobs
            .Where(x => candidateIds.Contains(x.Id) && !x.IsProcessed)
            .ToListAsync(cancellationToken))
            .Where(x => x.ClaimedUntilUtc == null || x.ClaimedUntilUtc <= now)
            .ToList();

        if (claimedRows.Count == 0)
        {
            await tx.CommitAsync(cancellationToken);
            return Array.Empty<IncomingPrintJob>();
        }

        foreach (var row in claimedRows)
        {
            row.ClaimedBy = _workerId;
            row.ClaimedUntilUtc = leaseEnd;
            row.ClaimToken = claimToken;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var jobs = claimedRows
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Select(row => new IncomingPrintJob(
                SourceJobId: row.Id,
                SourceSystem: _sourceSystem,
                ExternalJobId: row.ExternalJobId,
                StoreId: row.StoreId,
                DocumentType: row.DocumentType,
                Channel: string.IsNullOrWhiteSpace(row.Channel) ? "DEFAULT" : row.Channel,
                PdfBlob: row.PdfBlob,
                CreatedAtUtc: row.CreatedAtUtc))
            .ToList();

        _logger.LogInformation("SapHana adapter reclamó {Count} trabajos pendientes.", jobs.Count);
        return jobs;
    }

    public async Task MarkJobsProcessedAsync(
        IReadOnlyList<long> sourceJobIds,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.Mode, "SapHana", StringComparison.OrdinalIgnoreCase))
            return;

        if (sourceJobIds is null || sourceJobIds.Count == 0)
            return;

        var ids = sourceJobIds.Distinct().ToArray();
        var rows = await _dbContext.SourcePrintJobs
            .Where(x => ids.Contains(x.Id) && x.ClaimedBy == _workerId)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.IsProcessed = true;
            row.ClaimedBy = null;
            row.ClaimedUntilUtc = null;
            row.ClaimToken = null;
        }

        var affected = await _dbContext.SaveChangesAsync(cancellationToken);
        if (rows.Count < sourceJobIds.Count)
        {
            _logger.LogWarning(
                "SapHana ack parcial: solicitados={Expected}, actualizados={Rows}, affected={Affected}, worker={WorkerId}",
                sourceJobIds.Count,
                rows.Count,
                affected,
                _workerId);
        }
    }

    public async Task RenewJobLeasesAsync(IReadOnlyList<long> sourceJobIds, CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.Mode, "SapHana", StringComparison.OrdinalIgnoreCase))
            return;

        if (sourceJobIds is null || sourceJobIds.Count == 0)
            return;

        var leaseSeconds = Math.Max(15, _hanaOptions.LeaseSeconds);
        var newExpiry = DateTimeOffset.UtcNow.AddSeconds(leaseSeconds);
        var ids = sourceJobIds.Distinct().ToArray();

        await _dbContext.SourcePrintJobs
            .Where(x => ids.Contains(x.Id) && x.ClaimedBy == _workerId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.ClaimedUntilUtc, newExpiry),
                cancellationToken);
    }
}
