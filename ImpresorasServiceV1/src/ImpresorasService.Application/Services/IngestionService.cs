using System.Security.Cryptography;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Models;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ImpresorasService.Application.Services;

public class IngestionService
{
    private readonly IJobSourceAdapter _jobSourceAdapter;
    private readonly IPrintJobRepository _printJobRepository;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(
        IJobSourceAdapter jobSourceAdapter,
        IPrintJobRepository printJobRepository,
        ILogger<IngestionService> logger)
    {
        _jobSourceAdapter = jobSourceAdapter;
        _printJobRepository = printJobRepository;
        _logger = logger;
    }

    public async Task<int> IngestBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        IReadOnlyList<IncomingPrintJob> sourceJobs = await _jobSourceAdapter.FetchPendingJobsAsync(
            batchSize,
            cancellationToken);

        var insertedCount = 0;

        foreach (IncomingPrintJob sourceJob in sourceJobs)
        {
            bool alreadyExists = await _printJobRepository.ExistsBySourceExternalIdAsync(
                sourceJob.SourceSystem,
                sourceJob.ExternalJobId,
                cancellationToken);

            if (alreadyExists)
            {
                _logger.LogInformation(
                    "Duplicado descartado SourceSystem={SourceSystem} ExternalJobId={ExternalJobId}",
                    sourceJob.SourceSystem,
                    sourceJob.ExternalJobId);
                continue;
            }

            var jobId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

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
            insertedCount++;
        }

        await _printJobRepository.SaveChangesAsync(cancellationToken);
        return insertedCount;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
