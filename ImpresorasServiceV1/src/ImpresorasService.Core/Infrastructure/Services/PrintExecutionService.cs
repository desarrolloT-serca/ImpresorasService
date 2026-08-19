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
    private readonly IRoutingResolver _routingResolver;
    private readonly TimeProvider _timeProvider;

    public PrintExecutionService(
        ImpresorasDbContext db,
        IPrinterSpooler spooler,
        ILogger<PrintExecutionService> logger,
        IOptions<PrintExecutionOptions> options,
        IRoutingResolver routingResolver,
        TimeProvider timeProvider)
    {
        _db = db;
        _spooler = spooler;
        _logger = logger;
        _options = options.Value;
        _routingResolver = routingResolver;
        _timeProvider = timeProvider;
    }

    public async Task<int> ExecuteBatchAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        // "Printing" con más antigüedad que el timeout + buffer es un envío que quedó sin resolver.
        // Se recoge aquí para cerrarlo como PrintedUnknown (ver TryProcessOneAsync), NO para
        // reenviarlo: no hay forma de saber si el papel salió y un reenvío duplicaría el pedido.
        // El buffer evita tocar jobs cuyo spooler todavía está trabajando.
        var stalePrintingAfter = TimeSpan.FromSeconds(_options.TimeoutSeconds + 10);
        // Pending >2 min = IngestionService falló al enrutar; rescatar aquí para evitar huérfanos indefinidos.
        var stalePendingAfter = TimeSpan.FromMinutes(2);
        // Solo traer trabajos realmente elegibles. Si hacemos Take antes de comprobar NextRetryAtUtc,
        // muchos RetryScheduled todavia no vencidos pueden ocupar la ventana y dejar fuera reintentos listos.
        var candidates = await _db.PrintJobs
            .AsNoTracking()
            .Where(j =>
                j.Status == PrintJobStatus.Routed
                || (j.Status == PrintJobStatus.RetryScheduled && j.NextRetryAtUtc != null && j.NextRetryAtUtc <= now)
                || (j.Status == PrintJobStatus.Printing && j.UpdatedAtUtc <= now - stalePrintingAfter)
                || (j.Status == PrintJobStatus.Pending && j.UpdatedAtUtc <= now - stalePendingAfter))
            .OrderBy(j => j.NextRetryAtUtc ?? j.CreatedAtUtc)
            .ThenBy(j => j.CreatedAtUtc)
            .Take(batchSize)
            .Select(j => new
            {
                j.JobId,
                j.PrinterId,
                j.RowVersion,
                j.Status,
                j.NextRetryAtUtc,
                j.StoreId,
                j.DocumentType,
                j.Channel,
                j.CreatedAtUtc,
                j.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var eligible = candidates
            .Select(j => new { j.JobId, j.PrinterId, j.RowVersion, j.StoreId, j.DocumentType, j.Channel, j.Status })
            .ToList();

        var processed = 0;
        foreach (var item in eligible)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Rescate de Pending huérfanos: la ingesta los insertó pero el routing lanzó excepción.
            if (item.Status == PrintJobStatus.Pending)
            {
                await RescuePendingJobAsync(item.JobId, item.StoreId, item.DocumentType, item.Channel, item.RowVersion, cancellationToken);
                processed++;
                continue;
            }

            int printerIdToUse;
            if (item.PrinterId != null)
            {
                printerIdToUse = item.PrinterId.Value;
            }
            else
            {
                // Auto-curación: si el job está en Routed pero sin PrinterId, intentamos resolverlo.
                var resolved = await _routingResolver.ResolvePrinterAsync(
                    item.StoreId,
                    item.DocumentType,
                    item.Channel,
                    cancellationToken);

                if (resolved is null)
                {
                    await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
                    var job = await _db.PrintJobs.FirstOrDefaultAsync(j => j.JobId == item.JobId, cancellationToken);
                    if (job is not null)
                    {
                        if (!RowVersionSnapshotStillMatches(job.RowVersion, item.RowVersion))
                        {
                            // Otro worker modificó el job tras el barrido del lote; no pisar estado.
                            continue;
                        }

                        await TransitionToErrorFinalAsync(
                            job,
                            item.Status,
                            "ROUTE_NOT_FOUND",
                            "No existe regla activa aplicable para este trabajo (PrinterId nulo).",
                            cancellationToken);
                        await _db.SaveChangesAsync(cancellationToken);
                    }

                    await tx.CommitAsync(cancellationToken);
                    processed++;
                    continue;
                }

                printerIdToUse = resolved.Value;
            }

            var ok = await TryProcessOneAsync(item.JobId, printerIdToUse, item.RowVersion, cancellationToken);
            if (ok) processed++;
        }

        return processed;
    }

    private async Task<bool> TryProcessOneAsync(Guid jobId, int printerId, byte[] rowVersion, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var job = await _db.PrintJobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (job == null) return false;

        if (!RowVersionSnapshotStillMatches(job.RowVersion, rowVersion))
        {
            _logger.LogDebug(
                "JobId={JobId}: RowVersion del lote no coincide con BD; probable contienda entre workers.",
                jobId);
            return false;
        }

        // Auto-curación: si el job llegó con PrinterId null, lo fijamos para que el resto del flujo
        // (selección de candidatos por PrinterId) no se bloquee.
        if (job.PrinterId == null)
            job.PrinterId = printerId;

        var printer = await _db.Printers.FindAsync([printerId], ct);
        if (printer == null || !printer.IsActive)
        {
            await TransitionToErrorFinalAsync(job, job.Status, "PRINTER_INVALID", "Impresora inactiva o inexistente", ct);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }

        // Guard-rail: si el monitor de conectividad marca KO reciente, evitamos
        // enviar al spooler porque puede devolver "aceptado" aunque el dispositivo
        // fisico esté apagado/no alcanzable.
        if (printer.LastConnectionOk == false && printer.LastConnectionCheckAtUtc.HasValue)
        {
            var lastCheckAge = _timeProvider.GetUtcNow() - printer.LastConnectionCheckAtUtc.Value;
            if (lastCheckAge <= TimeSpan.FromSeconds(90))
            {
                var nextAttempt = job.AttemptCount + 1;
                if (nextAttempt < _options.MaxAttempts)
                {
                    var delaySec = _options.BackoffSeconds[Math.Min(nextAttempt - 1, _options.BackoffSeconds.Length - 1)];
                    var old = job.Status;
                    job.Status = PrintJobStatus.RetryScheduled;
                    job.AttemptCount = nextAttempt;
                    job.NextRetryAtUtc = _timeProvider.GetUtcNow().AddSeconds(delaySec);
                    job.LastErrorCode = "PRINTER_UNREACHABLE";
                    job.LastErrorMessage = printer.LastConnectionError ?? "Impresora no alcanzable (monitor de conectividad).";
                    job.UpdatedAtUtc = _timeProvider.GetUtcNow();
                    _db.PrintJobs.Update(job);
                    await _db.PrintJobEvents.AddAsync(new PrintJobEvent
                    {
                        JobId = job.JobId,
                        EventType = "StatusChanged",
                        OldStatus = old,
                        NewStatus = PrintJobStatus.RetryScheduled,
                        ErrorCode = "PRINTER_UNREACHABLE",
                        Message = job.LastErrorMessage,
                        ActorType = "system",
                        OccurredAtUtc = _timeProvider.GetUtcNow()
                    }, ct);
                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return true;
                }

                job.AttemptCount = nextAttempt;
                await TransitionToErrorFinalAsync(
                    job,
                    job.Status,
                    "PRINTER_UNREACHABLE",
                    printer.LastConnectionError ?? "Impresora no alcanzable (monitor de conectividad).",
                    ct);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            }
        }

        var stalePrintingAfter = TimeSpan.FromSeconds(_options.TimeoutSeconds + 10);
        if (job.Status == PrintJobStatus.Printing)
        {
            // Fresco: el envío al spooler sigue en curso, no tocar.
            if (job.UpdatedAtUtc > _timeProvider.GetUtcNow() - stalePrintingAfter)
                return false;

            // Stale: el proceso murió entre el COMMIT de Printing y el resultado del spooler.
            // La BD y la impresora no comparten transacción, así que aquí es imposible saber si
            // el papel llegó a salir. Reenviarlo automáticamente sacaría un segundo documento
            // cada vez que el envío anterior sí había prosperado.
            // Decisión de negocio: parar antes que duplicar. Se marca la incertidumbre y un
            // operador resuelve desde la cola (confirmar si salió, reimprimir si no, o cancelar).
            await TransitionToPrintedUnknownAsync(
                job,
                PrintJobStatus.Printing,
                "PRINTING_INTERRUPTED",
                "El envío a la impresora se interrumpió sin conocerse el resultado. Puede haberse impreso: compruébelo antes de reimprimir.",
                ct);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            _logger.LogWarning(
                "JobId={JobId}: Printing sin resolver tras {Seconds}s. Marcado PrintedUnknown; requiere decisión manual (no se reenvía para no duplicar).",
                jobId, stalePrintingAfter.TotalSeconds);
            return true;
        }
        else if (job.Status != PrintJobStatus.Routed && job.Status != PrintJobStatus.RetryScheduled)
        {
            return false;
        }

        if (job.Status == PrintJobStatus.RetryScheduled && job.NextRetryAtUtc > _timeProvider.GetUtcNow())
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
        job.UpdatedAtUtc = _timeProvider.GetUtcNow();
        _db.PrintJobs.Update(job);

        await _db.PrintJobEvents.AddAsync(new PrintJobEvent
        {
            JobId = jobId,
            EventType = "StatusChanged",
            OldStatus = oldStatus,
            NewStatus = PrintJobStatus.Printing,
            ActorType = "system",
            OccurredAtUtc = _timeProvider.GetUtcNow()
        }, ct);

        var rows = await _db.SaveChangesAsync(ct);
        if (rows == 0) return false;

        await tx.CommitAsync(ct);

        PrintSpoolResult result;
        try
        {
            if (job.PdfBlob is null || job.PdfBlob.Length == 0)
                result = new PrintSpoolResult(false, "PDF_MISSING", "PDF no disponible en la base de datos.", false);
            else
                result = await _spooler.SendToPrinterAsync(job.PdfBlob, printer.SpoolQueue, ct);
        }
        catch (OperationCanceledException)
        {
            // NO transitorio: ver la nota de WindowsPrintSpooler. El envío estaba en curso cuando
            // venció el plazo, así que no sabemos si salió el papel.
            result = new PrintSpoolResult(false, "NET_TIMEOUT", "Timeout de impresión", false);
        }
        catch (Exception ex)
        {
            // Nota de decisión: no volcamos ex.Message completo al estado final para evitar
            // que información interna se propague a UI/logs. Guardamos un mensaje genérico.
            _logger.LogError(ex, "Excepcion enviando a spooler. JobId={JobId} Printer={PrinterId}", jobId, printerId);
            result = new PrintSpoolResult(false, "SPOOLER_EXCEPTION", "Error en spooler", true);
        }

        await using var tx2 = await _db.Database.BeginTransactionAsync(ct);
        var job2 = await _db.PrintJobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (job2 == null || job2.Status != PrintJobStatus.Printing) return true;

        if (result.Success)
        {
            job2.Status = PrintJobStatus.SpoolAccepted;
            job2.LastErrorCode = null;
            job2.LastErrorMessage = null;
            job2.NextRetryAtUtc = null;
            job2.UpdatedAtUtc = _timeProvider.GetUtcNow();
            _db.PrintJobs.Update(job2);
            await _db.PrintJobEvents.AddAsync(new PrintJobEvent
            {
                JobId = jobId,
                EventType = "StatusChanged",
                OldStatus = PrintJobStatus.Printing,
                NewStatus = PrintJobStatus.SpoolAccepted,
                ActorType = "system",
                Message = "Spooler aceptó el trabajo",
                OccurredAtUtc = _timeProvider.GetUtcNow()
            }, ct);
        }
        else if (result.ErrorCode == "NET_TIMEOUT")
        {
            // El envío se cortó a medias: pudo haber llegado a la cola de Windows. Ni error (afirmaría
            // que no se imprimió) ni reintento (duplicaría). Mismo tratamiento que el Printing stale:
            // se marca la incertidumbre y decide un operador desde la cola.
            await TransitionToPrintedUnknownAsync(
                job2,
                PrintJobStatus.Printing,
                result.ErrorCode,
                "El envío a la impresora se agotó de tiempo sin conocerse el resultado. Puede haberse impreso: compruébelo antes de reimprimir.",
                ct);
            _logger.LogWarning(
                "JobId={JobId}: timeout de impresión. Marcado PrintedUnknown; requiere decisión manual (no se reenvía para no duplicar).",
                jobId);
        }
        else if (result.IsTransient && job2.AttemptCount < _options.MaxAttempts)
        {
            var delaySec = _options.BackoffSeconds[Math.Min(job2.AttemptCount - 1, _options.BackoffSeconds.Length - 1)];
            job2.Status = PrintJobStatus.RetryScheduled;
            job2.NextRetryAtUtc = _timeProvider.GetUtcNow().AddSeconds(delaySec);
            job2.LastErrorCode = result.ErrorCode;
            job2.LastErrorMessage = result.ErrorMessage;
            job2.UpdatedAtUtc = _timeProvider.GetUtcNow();
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
                OccurredAtUtc = _timeProvider.GetUtcNow()
            }, ct);
        }
        else
        {
            var errCode = result.ErrorCode ?? "UNKNOWN";
            var errMsg = result.ErrorMessage ?? "Error desconocido";
            _logger.LogWarning("Impresion fallida JobId={JobId} Printer={Printer} Code={Code} Msg={Msg}",
                jobId, printer.SpoolQueue, errCode, errMsg);
            await TransitionToErrorFinalAsync(job2, PrintJobStatus.Printing, errCode, errMsg, ct);
        }

        await _db.SaveChangesAsync(ct);
        await tx2.CommitAsync(ct);
        return true;
    }

    private async Task RescuePendingJobAsync(Guid jobId, int storeId, string documentType, string channel, byte[] rowVersion, CancellationToken ct)
    {
        var resolved = await _routingResolver.ResolvePrinterAsync(storeId, documentType, channel, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Solo RowVersion para el check de concurrencia; evitar cargar PdfBlob que no se necesita en el rescate.
        var versionRow = await _db.PrintJobs.AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.RowVersion })
            .FirstOrDefaultAsync(ct);

        if (versionRow is null) { await tx.CommitAsync(ct); return; }
        if (!RowVersionSnapshotStillMatches(versionRow.RowVersion, rowVersion)) { await tx.CommitAsync(ct); return; }

        var now = _timeProvider.GetUtcNow();
        // Attach + mark-modified: no carga PdfBlob, no lo sobreescribe.
        var entity = new PrintJob { JobId = jobId };
        _db.PrintJobs.Attach(entity);

        if (resolved is null)
        {
            entity.Status = PrintJobStatus.ErrorFinal;
            entity.LastErrorCode = "ROUTE_NOT_FOUND";
            entity.LastErrorMessage = "No existe regla activa aplicable para este trabajo.";
            entity.NextRetryAtUtc = null;
            entity.UpdatedAtUtc = now;
            _db.Entry(entity).Property(x => x.Status).IsModified = true;
            _db.Entry(entity).Property(x => x.LastErrorCode).IsModified = true;
            _db.Entry(entity).Property(x => x.LastErrorMessage).IsModified = true;
            _db.Entry(entity).Property(x => x.NextRetryAtUtc).IsModified = true;
            _db.Entry(entity).Property(x => x.UpdatedAtUtc).IsModified = true;
            await _db.PrintJobEvents.AddAsync(new PrintJobEvent
            {
                JobId = jobId,
                EventType = "StatusChanged",
                OldStatus = PrintJobStatus.Pending,
                NewStatus = PrintJobStatus.ErrorFinal,
                ErrorCode = "ROUTE_NOT_FOUND",
                Message = "No existe regla activa aplicable para este trabajo.",
                ActorType = "system",
                OccurredAtUtc = now
            }, ct);
        }
        else
        {
            entity.Status = PrintJobStatus.Routed;
            entity.PrinterId = resolved;
            entity.UpdatedAtUtc = now;
            _db.Entry(entity).Property(x => x.Status).IsModified = true;
            _db.Entry(entity).Property(x => x.PrinterId).IsModified = true;
            _db.Entry(entity).Property(x => x.UpdatedAtUtc).IsModified = true;
            await _db.PrintJobEvents.AddAsync(new PrintJobEvent
            {
                JobId = jobId,
                EventType = "ROUTED",
                OldStatus = PrintJobStatus.Pending,
                NewStatus = PrintJobStatus.Routed,
                ActorType = "system",
                Message = $"Re-enrutado (rescate de Pending huérfano) a impresora {resolved}.",
                OccurredAtUtc = now
            }, ct);
            _logger.LogInformation("Rescate Pending: job {JobId} enrutado a impresora {PrinterId}.", jobId, resolved);
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Cierra el trabajo en incertidumbre: el sistema no puede afirmar si el documento salió.
    /// No es un error (el envío pudo completarse) ni un éxito, y por eso no se reintenta solo.
    /// </summary>
    private async Task TransitionToPrintedUnknownAsync(PrintJob job, PrintJobStatus oldStatus, string errorCode, string message, CancellationToken ct)
    {
        job.Status = PrintJobStatus.PrintedUnknown;
        job.LastErrorCode = errorCode;
        job.LastErrorMessage = message;
        job.NextRetryAtUtc = null;
        job.UpdatedAtUtc = _timeProvider.GetUtcNow();
        _db.PrintJobs.Update(job);
        await _db.PrintJobEvents.AddAsync(new PrintJobEvent
        {
            JobId = job.JobId,
            EventType = "StatusChanged",
            OldStatus = oldStatus,
            NewStatus = PrintJobStatus.PrintedUnknown,
            ErrorCode = errorCode,
            Message = message,
            ActorType = "system",
            OccurredAtUtc = _timeProvider.GetUtcNow()
        }, ct);
    }

    private async Task TransitionToErrorFinalAsync(PrintJob job, PrintJobStatus oldStatus, string errorCode, string message, CancellationToken ct)
    {
        job.Status = PrintJobStatus.ErrorFinal;
        job.LastErrorCode = errorCode;
        job.LastErrorMessage = message;
        job.NextRetryAtUtc = null;
        job.UpdatedAtUtc = _timeProvider.GetUtcNow();
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
            OccurredAtUtc = _timeProvider.GetUtcNow()
        }, ct);
    }

    /// <summary>
    /// El barrido del lote guardó un RowVersion; si la fila cambió desde entonces, no debemos avanzar.
    /// Con token vacío en ambos lados se acepta (filas legadas antes del primer guardado con versión).
    /// </summary>
    private static bool RowVersionSnapshotStillMatches(byte[] currentInDb, byte[] snapshotFromBatch)
    {
        var snap = snapshotFromBatch ?? Array.Empty<byte>();
        var cur = currentInDb ?? Array.Empty<byte>();
        if (snap.Length == 0)
            return cur.Length == 0;

        return cur.AsSpan().SequenceEqual(snap);
    }
}
