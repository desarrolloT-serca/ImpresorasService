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

    public Task<bool> ExistsBySourceExternalIdAsync(
        string sourceSystem,
        string externalJobId,
        CancellationToken cancellationToken)
    {
        return _dbContext.PrintJobs.AnyAsync(
            x => x.SourceSystem == sourceSystem && x.ExternalJobId == externalJobId,
            cancellationToken);
    }

    public async Task AddAsync(PrintJob printJob, CancellationToken cancellationToken)
    {
        await _dbContext.PrintJobs.AddAsync(printJob, cancellationToken);
    }

    public async Task AddEventAsync(PrintJobEvent printJobEvent, CancellationToken cancellationToken)
    {
        await _dbContext.PrintJobEvents.AddAsync(printJobEvent, cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
