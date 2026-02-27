using ImpresorasService.Application.Models;

namespace ImpresorasService.Application.Abstractions;

public interface IJobSourceAdapter
{
    Task<IReadOnlyList<IncomingPrintJob>> FetchPendingJobsAsync(int batchSize, CancellationToken cancellationToken);
}
