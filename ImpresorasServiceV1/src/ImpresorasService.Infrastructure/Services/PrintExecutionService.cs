using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Services;

public sealed class PrintExecutionService : IPrintExecutionService
{
    private readonly ImpresorasDbContext _db;
    private readonly IPrinterSpooler _spooler;
    private readonly ILogger<PrintExecutionService> _logger;
    private readonly PrintExecutionOptions _options;

    public PrintExecutionService(
        ImpresorasDbContext db,
        IPrinterSpooler spooler,
        ILogger<PrintExecutionService> logger,
        IOptions<PrintExecutionOptions> options)
    {
        _db = db;
        _spooler = spooler;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<int> ExecuteBatchAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var maxAttempts = _options.MaxAttempts;
        // Cargar y filtrar en memoria: SQLite no traduce bien enum/Status ni DateTimeOffset en ORDER BY
        var candidates = await _db.PrintJobs
            .AsNoTracking()
            .Where(j => j.PrinterId != null && j.AttemptCount < maxAttempts)
            .Take(batchSize * 4)
            .Select(j => new { j.JobId, j.PrinterId, j.RowVersion, j.Status, j.NextRetryAtUtc, j.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var eligible = candidates
            .Where(j => j.Status == PrintJobStatus.Routed
                        || (j.Status == PrintJobStatus.RetryScheduled && j.NextRetryAtUtc != null && j.NextRetryAtUtc <= now))
            .OrderBy(j => j.CreatedAtUtc)
            .Take(batchSize)
            .Select(j => new { j.JobId, j.PrinterId, j.RowVersion })
            .ToList();

        var processed = 0;
        foreach (var item in eligible)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var ok = await TryProcessOneAsync(item.JobId, item.PrinterId!.Value, item.RowVersion, cancellationToken);
            if (ok) processed++;
        }

        return processed;
    }

    private async Task<bool> TryProcessOneAsync(Guid jobId, int printerId, byte[] rowVersion, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var job = await _db.PrintJobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (job == null) return false;

        var printer = await _db.Printers.FindAsync([printerId], ct);
        if (printer == null || !printer.IsActive)
        {
            await TransitionToErrorFinalAsync(job, job.Status, "PRINTER_INVALID", "Impresora inactiva o inexistente", ct);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }

        if (job.Status != PrintJobStatus.Routed && job.Status != PrintJobStatus.RetryScheduled)
            return false;

        if (job.Status == PrintJobStatus.RetryScheduled && job.NextRetryAtUtc > DateTimeOffset.UtcNow)
            return false;

        if (job.AttemptCount >= _options.MaxAttempts)
        {
            await TransitionToErrorFinalAsync(job, job.Status, "RETRIES_EXHAUSTED", "Intentos agotados", ct);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }

        var oldStatus = job.Status;
        job.Status = PrintJobStatus.Printing;
        job.AttemptCount++;
        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        _db.PrintJobs.Update(job);

        await _db.PrintJobEvents.AddAsync(new PrintJobEvent
        {
            JobId = jobId,
            EventType = "StatusChanged",
            OldStatus = oldStatus,
            NewStatus = PrintJobStatus.Printing,
            ActorType = "system",
            OccurredAtUtc = DateTimeOffset.UtcNow
        }, ct);

        try
        {
            var rows = await _db.SaveChangesAsync(ct);
            if (rows == 0) return false;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }

        await tx.CommitAsync(ct);

        var result = await _spooler.SendToPrinterAsync(job.PdfBlob, printer.SpoolQueue, ct);

        await using var tx2 = await _db.Database.BeginTransactionAsync(ct);
        var job2 = await _db.PrintJobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (job2 == null || job2.Status != PrintJobStatus.Printing) return true;

        if (result.Success)
        {
            job2.Status = PrintJobStatus.SpoolAccepted;
            job2.LastErrorCode = null;
            job2.LastErrorMessage = null;
            job2.NextRetryAtUtc = null;
            job2.UpdatedAtUtc = DateTimeOffset.UtcNow;
            _db.PrintJobs.Update(job2);
            await _db.PrintJobEvents.AddAsync(new PrintJobEvent
            {
                JobId = jobId,
                EventType = "StatusChanged",
                OldStatus = PrintJobStatus.Printing,
                NewStatus = PrintJobStatus.SpoolAccepted,
                ActorType = "system",
                Message = "Spooler aceptó el trabajo",
                OccurredAtUtc = DateTimeOffset.UtcNow
            }, ct);
        }
        else if (result.IsTransient && job2.AttemptCount < _options.MaxAttempts)
        {
            var delaySec = _options.BackoffSeconds[Math.Min(job2.AttemptCount - 1, _options.BackoffSeconds.Length - 1)];
            job2.Status = PrintJobStatus.RetryScheduled;
            job2.NextRetryAtUtc = DateTimeOffset.UtcNow.AddSeconds(delaySec);
            job2.LastErrorCode = result.ErrorCode;
            job2.LastErrorMessage = result.ErrorMessage;
            job2.UpdatedAtUtc = DateTimeOffset.UtcNow;
            _db.PrintJobs.Update(job2);
            await _db.PrintJobEvents.AddAsync(new PrintJobEvent
            {
                JobId = jobId,
                EventType = "StatusChanged",
                OldStatus = PrintJobStatus.Printing,
                NewStatus = PrintJobStatus.RetryScheduled,
                ErrorCode = result.ErrorCode,
                Message = result.ErrorMessage,
                ActorType = "system",
                OccurredAtUtc = DateTimeOffset.UtcNow
            }, ct);
        }
        else
        {
            await TransitionToErrorFinalAsync(job2, PrintJobStatus.Printing, result.ErrorCode ?? "UNKNOWN", result.ErrorMessage ?? "Error desconocido", ct);
        }

        await _db.SaveChangesAsync(ct);
        await tx2.CommitAsync(ct);
        return true;
    }

    private async Task TransitionToErrorFinalAsync(PrintJob job, PrintJobStatus oldStatus, string errorCode, string message, CancellationToken ct)
    {
        job.Status = PrintJobStatus.ErrorFinal;
        job.LastErrorCode = errorCode;
        job.LastErrorMessage = message;
        job.NextRetryAtUtc = null;
        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        _db.PrintJobs.Update(job);
        await _db.PrintJobEvents.AddAsync(new PrintJobEvent
        {
            JobId = job.JobId,
            EventType = "StatusChanged",
            OldStatus = oldStatus,
            NewStatus = PrintJobStatus.ErrorFinal,
            ErrorCode = errorCode,
            Message = message,
            ActorType = "system",
            OccurredAtUtc = DateTimeOffset.UtcNow
        }, ct);
    }
}
