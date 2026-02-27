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

    public SqlTestJobSourceAdapter(
        IOptions<SourceOptions> options,
        ImpresorasDbContext dbContext,
        ILogger<SqlTestJobSourceAdapter> logger)
    {
        _options = options.Value;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<IncomingPrintJob>> FetchPendingJobsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.Mode, "SqlTest", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<IncomingPrintJob>();
        }

        var sourceRows = await _dbContext.SourcePrintJobs
            .Where(x => !x.IsProcessed)
            .OrderBy(x => x.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (sourceRows.Count == 0)
        {
            return Array.Empty<IncomingPrintJob>();
        }

        var jobs = new List<IncomingPrintJob>(sourceRows.Count);
        foreach (SourcePrintJobRecord row in sourceRows)
        {
            row.IsProcessed = true;
            jobs.Add(new IncomingPrintJob(
                SourceSystem: row.SourceSystem,
                ExternalJobId: row.ExternalJobId,
                StoreId: row.StoreId,
                DocumentType: row.DocumentType,
                Channel: string.IsNullOrWhiteSpace(row.Channel) ? "DEFAULT" : row.Channel,
                PdfBlob: row.PdfBlob,
                CreatedAtUtc: row.CreatedAtUtc));
        }

        _logger.LogInformation("SqlTest adapter leyo {Count} trabajos pendientes.", jobs.Count);
        return jobs;
    }
}
