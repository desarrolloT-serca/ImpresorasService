using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Infrastructure.Repositories;

public class PrintJobRepository : IPrintJobRepository
{
    private readonly ImpresorasDbContext _dbContext;

    public PrintJobRepository(ImpresorasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistsBySourceExternalIdAsync(
        string sourceSystem,
        string externalJobId,
        CancellationToken cancellationToken)
    {
        // Workaround para SAP EF provider (HANA): AnyAsync puede lanzar
        // "Sequence contains no elements" en consultas EXISTS.
        return await _dbContext.PrintJobs
            .Where(x => x.SourceSystem == sourceSystem && x.ExternalJobId == externalJobId)
            .Select(x => x.JobId)
            .Take(1)
            .CountAsync(cancellationToken) > 0;
    }

    public async Task AddAsync(PrintJob printJob, CancellationToken cancellationToken)
    {
        await _dbContext.PrintJobs.AddAsync(printJob, cancellationToken);
    }

    public async Task AddEventAsync(PrintJobEvent printJobEvent, CancellationToken cancellationToken)
    {
        await _dbContext.PrintJobEvents.AddAsync(printJobEvent, cancellationToken);
    }

    public async Task MarkSourcePrintJobsProcessedAsync(
        IReadOnlyList<long> sourceJobIds,
        CancellationToken cancellationToken)
    {
        if (sourceJobIds is null || sourceJobIds.Count == 0)
            return;

        // Nota de decisión: cargamos y marcamos en memoria en vez de ExecuteUpdate para mantener
        // la operación dentro del mismo SaveChangesAsync del caller (atomicidad con PrintJobs).
        var rows = await _dbContext.SourcePrintJobs
            .Where(x => sourceJobIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
            row.IsProcessed = true;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public void ClearTracking()
    {
        _dbContext.ChangeTracker.Clear();
    }
}
