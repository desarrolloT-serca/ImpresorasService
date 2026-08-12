using System.Security.Cryptography;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Models;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ImpresorasService.Application.Services;

public class IngestionService
{
    private readonly IJobSourceAdapter _jobSourceAdapter;
    private readonly IPrintJobRepository _printJobRepository;
    private readonly IRoutingService _routingService;
    private readonly ILogger<IngestionService> _logger;
    private readonly TimeProvider _timeProvider;

    public IngestionService(
        IJobSourceAdapter jobSourceAdapter,
        IPrintJobRepository printJobRepository,
        IRoutingService routingService,
        ILogger<IngestionService> logger,
        TimeProvider timeProvider)
    {
        _jobSourceAdapter = jobSourceAdapter;
        _printJobRepository = printJobRepository;
        _routingService = routingService;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<int> IngestBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        IReadOnlyList<IncomingPrintJob> sourceJobs = await _jobSourceAdapter.FetchPendingJobsAsync(
            batchSize,
            cancellationToken);

        var insertedCount = 0;
        var duplicatesCount = 0;
        var failedCount = 0;
        var insertedJobIds = new List<Guid>();
        var sourceJobIdsToMarkProcessed = new List<long>(sourceJobs.Count);

        foreach (IncomingPrintJob sourceJob in sourceJobs)
        {
            // El id se añade a la lista de ack solo cuando el trabajo queda REALMENTE resuelto
            // (insertado o confirmado como duplicado ya presente). Añadirlo aquí, antes de intentar
            // nada, hacía que un fallo de persistencia acabara marcando el source row como
            // procesado: el PDF se perdía sin traza y sin posibilidad de reintento.
            bool alreadyExists = await _printJobRepository.ExistsBySourceExternalIdAsync(
                sourceJob.SourceSystem,
                sourceJob.ExternalJobId,
                cancellationToken);

            if (alreadyExists)
            {
                // Duplicado real: el destino ya lo tiene, el origen puede darse por servido.
                sourceJobIdsToMarkProcessed.Add(sourceJob.SourceJobId);
                duplicatesCount++;
                _logger.LogInformation(
                    "Duplicado descartado SourceSystem={SourceSystem} ExternalJobId={ExternalJobId}",
                    sourceJob.SourceSystem,
                    sourceJob.ExternalJobId);
                continue;
            }

            var jobId = Guid.NewGuid();
            var now = _timeProvider.GetUtcNow();

            var printJob = new PrintJob
            {
                JobId = jobId,
                SourceSystem = sourceJob.SourceSystem,
                ExternalJobId = sourceJob.ExternalJobId,
                StoreId = sourceJob.StoreId,
                DocumentType = sourceJob.DocumentType,
                Channel = string.IsNullOrWhiteSpace(sourceJob.Channel) ? "DEFAULT" : sourceJob.Channel,
                PdfBlob = sourceJob.PdfBlob,
                PdfSha256 = ComputeSha256(sourceJob.PdfBlob),
                Status = PrintJobStatus.Pending,
                AttemptCount = 0,
                CorrelationId = Guid.NewGuid(),
                CreatedAtUtc = sourceJob.CreatedAtUtc == default ? now : sourceJob.CreatedAtUtc,
                UpdatedAtUtc = now
            };

            var createdEvent = new PrintJobEvent
            {
                JobId = printJob.JobId,
                EventType = "INGESTED",
                OldStatus = null,
                NewStatus = PrintJobStatus.Pending,
                ActorType = "system",
                Message = "Trabajo ingerido desde origen y encolado en PrintJobs.",
                OccurredAtUtc = now
            };

            await _printJobRepository.AddAsync(printJob, cancellationToken);
            await _printJobRepository.AddEventAsync(createdEvent, cancellationToken);

            try
            {
                // Fase 1.4: guardar por job, no en un único SaveChanges de lote. Dos jobs del
                // mismo fetch con igual (SourceSystem, ExternalJobId) pasan ambos el chequeo
                // ExistsBySourceExternalIdAsync porque ninguno está aún persistido; con guardado
                // en lote, la violación del índice único abortaba el lote entero.
                await _printJobRepository.SaveChangesAsync(cancellationToken);
                sourceJobIdsToMarkProcessed.Add(sourceJob.SourceJobId);
                insertedJobIds.Add(jobId);
                insertedCount++;
            }
            catch (DbUpdateException ex)
            {
                _printJobRepository.ClearTracking();

                // DbUpdateException no significa "violación de índice único": también cubre
                // timeouts, caídas de conexión y otros constraints. Solo el destino puede decir
                // cuál fue: si el job está ahí, era un duplicado intra-lote y el origen puede
                // acusarse; si no está, la inserción falló de verdad y el source row debe quedar
                // sin ack para que el lease expire y otro ciclo lo reintente.
                bool persistedByAnotherAttempt;
                try
                {
                    persistedByAnotherAttempt = await _printJobRepository.ExistsBySourceExternalIdAsync(
                        sourceJob.SourceSystem,
                        sourceJob.ExternalJobId,
                        cancellationToken);
                }
                catch (Exception probeEx)
                {
                    // Sin poder comprobarlo, la opción segura es no acusar recibo: reingerir un
                    // duplicado lo frena el índice único, perder el PDF no lo frena nada.
                    _logger.LogError(probeEx,
                        "No se pudo verificar si el trabajo quedó persistido; no se acusa recibo en origen. ExternalJobId={ExternalJobId}",
                        sourceJob.ExternalJobId);
                    persistedByAnotherAttempt = false;
                }

                if (persistedByAnotherAttempt)
                {
                    sourceJobIdsToMarkProcessed.Add(sourceJob.SourceJobId);
                    duplicatesCount++;
                    _logger.LogWarning(ex,
                        "Duplicado intra-lote descartado tras violar restricción única. SourceSystem={SourceSystem} ExternalJobId={ExternalJobId}",
                        sourceJob.SourceSystem,
                        sourceJob.ExternalJobId);
                }
                else
                {
                    failedCount++;
                    _logger.LogError(ex,
                        "Fallo al persistir trabajo ingerido; el origen NO se marca procesado y se reintentará al expirar el lease. SourceSystem={SourceSystem} ExternalJobId={ExternalJobId}",
                        sourceJob.SourceSystem,
                        sourceJob.ExternalJobId);
                }
            }
        }

        if (sourceJobIdsToMarkProcessed.Count > 0)
            await _jobSourceAdapter.RenewJobLeasesAsync(sourceJobIdsToMarkProcessed, cancellationToken);

        // Nota de decisión: el "ack" en el origen (remoto o local) se delega al adapter y
        // se ejecuta solo cuando el caller ha persistido la cola local con éxito.
        await _jobSourceAdapter.MarkJobsProcessedAsync(sourceJobIdsToMarkProcessed, cancellationToken);

        _logger.LogInformation(
            "Ingestión lote completada. fetched={Fetched} inserted={Inserted} duplicates={Duplicates} failed={Failed} acked={Acked}",
            sourceJobs.Count,
            insertedCount,
            duplicatesCount,
            failedCount,
            sourceJobIdsToMarkProcessed.Count);

        // Enrutado automático: una carga de reglas/impresoras para todo el lote (AUD-17)
        try
        {
            await _routingService.TryRouteBatchAsync(insertedJobIds, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al enrutar lote de {Count} trabajos.", insertedJobIds.Count);
        }

        return insertedCount;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
