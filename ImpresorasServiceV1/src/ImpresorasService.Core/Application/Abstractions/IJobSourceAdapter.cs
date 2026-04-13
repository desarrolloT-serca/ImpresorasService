using ImpresorasService.Application.Models;

namespace ImpresorasService.Application.Abstractions;

public interface IJobSourceAdapter
{
    Task<IReadOnlyList<IncomingPrintJob>> FetchPendingJobsAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Marca como "procesado" en el origen remoto/local los jobs que fueron reclutados vía FetchPendingJobsAsync.
    /// Debe ejecutarse solo después de que el job haya sido insertado en la cola local (PrintJobs + Events)
    /// con éxito.
    /// </summary>
    Task MarkJobsProcessedAsync(IReadOnlyList<long> sourceJobIds, CancellationToken cancellationToken);

    /// <summary>
    /// Extiende el arrendamiento en el origen para los ids ya reclamados (evita expiración durante ingesta lenta).
    /// </summary>
    Task RenewJobLeasesAsync(IReadOnlyList<long> sourceJobIds, CancellationToken cancellationToken);
}
