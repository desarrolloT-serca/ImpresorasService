using ImpresorasService.Domain.Entities;

namespace ImpresorasService.Application.Abstractions;

public interface IPrintJobRepository
{
    Task<bool> ExistsBySourceExternalIdAsync(
        string sourceSystem,
        string externalJobId,
        CancellationToken cancellationToken);

    Task AddAsync(PrintJob printJob, CancellationToken cancellationToken);
    Task AddEventAsync(PrintJobEvent printJobEvent, CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
