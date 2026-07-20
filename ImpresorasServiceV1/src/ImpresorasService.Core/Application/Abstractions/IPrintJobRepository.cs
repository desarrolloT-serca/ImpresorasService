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
    /// <summary>
    /// Marca los SourcePrintJobs como procesados. El guardado final debe hacerse con SaveChangesAsync.
    /// </summary>
    Task MarkSourcePrintJobsProcessedAsync(
        IReadOnlyList<long> sourceJobIds,
        CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Descarta el tracking de cambios pendientes tras un SaveChangesAsync fallido (p.ej. violación
    /// de índice único), para poder seguir insertando el resto del lote sin arrastrar la entidad rota.
    /// </summary>
    void ClearTracking();
}
