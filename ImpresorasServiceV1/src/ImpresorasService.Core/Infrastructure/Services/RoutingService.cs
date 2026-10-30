using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Infrastructure.Services;

public class RoutingService : IRoutingService
{
    private readonly ImpresorasDbContext _db;
    private readonly IRoutingResolver _resolver;

    public const string RouteNotFoundCode = "ROUTE_NOT_FOUND";
    /// <summary>Reintento que pide un operador de algo que no había salido (venía de ErrorFinal).</summary>
    public const string RetryRouteEvent = "RETRY_ROUTE";
    /// <summary>
    /// Reimpresión de algo que ya salió, con o sin confirmación: puede quedar duplicado. Lleva quién
    /// (actor_id), el motivo y la nota (metadata_json) y es el registro que consulta GET /api/reprints.
    /// </summary>
    public const string ReprintEvent = "REPRINT";
    public const string OtherReason = "OTRO";
    /// <summary>Motivos de reimpresión: clave (en metadata_json) y texto. «Otro» exige nota.</summary>
    public static readonly IReadOnlyDictionary<string, string> ReprintReasons = new Dictionary<string, string>
    {
        ["MAL_IMPRESO"] = "Mal impreso",
        ["PERDIDO"] = "Perdido",
        ["DANADO"] = "Dañado o mojado",
        ["ATASCO"] = "Atasco o sin papel",
        [OtherReason] = "Otro",
    };
    /// <summary>Lo que ya salió se reimprime con motivo; no se reintenta.</summary>
    public static readonly PrintJobStatus[] ReprintableStatuses = [PrintJobStatus.PrintedConfirmed, PrintJobStatus.PrintedUnknown];
    public const int MaxReprintNoteLength = 500;

    public RoutingService(ImpresorasDbContext db, IRoutingResolver resolver)
    {
        _db = db;
        _resolver = resolver;
    }

    /// <summary>
    /// Reintento manual de algo que no ha salido (Pending o ErrorFinal: ROUTE_NOT_FOUND,
    /// PRINTER_INVALID, RETRIES_EXHAUSTED...). Lo que ya salió va por <see cref="ReprintAsync"/>,
    /// con motivo: por aquí no, para que ninguna copia posible quede sin registrar.
    /// </summary>
    public async Task<RouteResult> TryRetryRouteAsync(Guid jobId, CancellationToken cancellationToken = default, string? actorId = null)
    {
        var job = await ReadJobAsync(jobId, cancellationToken);

        if (job.Status == PrintJobStatus.ErrorFinal)
        {
            // Sin PDF (no se pudo descargar, o la retención ya lo borró) acabaría en PDF_MISSING.
            if (!await HasPdfAsync(jobId, cancellationToken))
                throw new InvalidOperationException(SinPdf);
            job = await ResetForResendAsync(job, new PrintJobEvent
            {
                EventType = RetryRouteEvent,
                ActorId = actorId,
                Message = "Reintento manual solicitado desde ErrorFinal."
            }, cancellationToken);
        }
        else if (ReprintableStatuses.Contains(job.Status))
        {
            throw new InvalidOperationException("Este trabajo ya salió: se reimprime con motivo, no se reintenta.");
        }
        else if (job.Status != PrintJobStatus.Pending)
        {
            throw new InvalidOperationException("Solo se reintenta lo que acabó en error; este trabajo sigue en curso o se canceló.");
        }

        return await TryRouteJobInternalAsync(job, cancellationToken);
    }

    private const string SinPdf =
        "Este trabajo ya no tiene el PDF (no se pudo descargar, o se borró a los 90 días): el ERP tiene que volver a mandar el documento.";

    // El trabajo sin su PDF, que puede pesar megas: para decidir y enrutar no hace falta (auditoría L7).
    private async Task<PrintJob> ReadJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        await _db.PrintJobs.AsNoTracking()
            .Where(x => x.JobId == jobId)
            .Select(x => new PrintJob
            {
                JobId = x.JobId, Status = x.Status, PrinterId = x.PrinterId, StoreId = x.StoreId,
                DocumentType = x.DocumentType, Channel = x.Channel
            })
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException($"Job {jobId} no encontrado.");

    private Task<bool> HasPdfAsync(Guid jobId, CancellationToken cancellationToken) =>
        _db.PrintJobs.AnyAsync(x => x.JobId == jobId && x.PdfBlob != null, cancellationToken);

    /// <summary>
    /// Reimprime algo que ya salió (PrintedConfirmed o PrintedUnknown): mal impreso, perdido,
    /// mojado... Sin límite de veces (decisión del usuario, 26/10/2026), pero siempre con motivo y
    /// con quién, en un evento REPRINT. Sin PDF (retención) no se puede: lo vuelve a mandar el ERP.
    /// </summary>
    public async Task<RouteResult> ReprintAsync(
        Guid jobId, string? reason, string? note, string? actorId, CancellationToken cancellationToken = default)
    {
        if (reason is null || !ReprintReasons.TryGetValue(reason, out var reasonText))
            throw new InvalidOperationException("Motivo de reimpresión no válido.");
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (reason == OtherReason && note is null)
            throw new InvalidOperationException("Con «Otro» hay que explicar el motivo.");
        if (note?.Length > MaxReprintNoteLength)
            throw new InvalidOperationException($"La nota admite como mucho {MaxReprintNoteLength} caracteres.");

        var job = await ReadJobAsync(jobId, cancellationToken);
        if (!ReprintableStatuses.Contains(job.Status))
            throw new InvalidOperationException("Solo se reimprime lo que ya salió (impreso o sin confirmar).");
        if (!await HasPdfAsync(jobId, cancellationToken))
            throw new InvalidOperationException(SinPdf);

        job = await ResetForResendAsync(job, new PrintJobEvent
        {
            EventType = ReprintEvent,
            ActorId = actorId,
            Message = $"Reimpresión: {reasonText}{(note is null ? "" : $" ({note})")}. Ya había salido: puede quedar duplicado.",
            MetadataJson = JsonSerializer.Serialize(new { reason, note, printerId = job.PrinterId })
        }, cancellationToken);

        return await TryRouteJobInternalAsync(job, cancellationToken);
    }

    /// <summary>
    /// Devuelve el trabajo a Pending para volver a mandarlo y apunta el evento. Fase 2.5: UPDATE
    /// condicionado al estado leído, así que si el Worker lo cambió entre medias afecta a 0 filas y se
    /// responde con la verdad, en vez de confirmar un cambio que nunca llegó a aplicarse. También al
    /// PDF: si la retención lo borró entre la comprobación y aquí, no se reenvía (auditoría L6). El
    /// UPDATE y su evento, juntos o ninguno: una copia nunca queda sin registrar (auditoría L3).
    /// </summary>
    private async Task<PrintJob> ResetForResendAsync(PrintJob job, PrintJobEvent evento, CancellationToken cancellationToken)
    {
        var previousStatus = job.Status;
        var resetAt = DateTimeOffset.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        var reset = await _db.PrintJobs
            .Where(x => x.JobId == job.JobId && x.Status == previousStatus && x.PdfBlob != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, PrintJobStatus.Pending)
                .SetProperty(x => x.PrinterId, (int?)null)
                // El número de la impresora era del envío anterior: con él, el watchdog
                // confirmaría la copia nueva por el trabajo viejo.
                .SetProperty(x => x.IppJobId, (int?)null)
                .SetProperty(x => x.AttemptCount, 0)
                .SetProperty(x => x.NextRetryAtUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.LastErrorCode, (string?)null)
                .SetProperty(x => x.LastErrorMessage, (string?)null)
                .SetProperty(x => x.UpdatedAtUtc, resetAt)
                // La cola ordena por aquí: lo que se vuelve a mandar sube arriba aunque no salga.
                .SetProperty(x => x.LastRequeuedAtUtc, resetAt), cancellationToken);

        if (reset != 1)
            throw new PrintJobStateConflictException(
                $"El job {job.JobId} cambio de estado mientras se preparaba el reenvío; vuelva a consultarlo.");

        evento.JobId = job.JobId;
        evento.OldStatus = previousStatus;
        evento.NewStatus = PrintJobStatus.Pending;
        evento.ActorType = "user";
        evento.OccurredAtUtc = resetAt;
        _db.PrintJobEvents.Add(evento);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // ExecuteUpdate no refresca lo ya leido: sin esto el enrutado seguiria viendo el
        // estado anterior y trataria de transicionar desde el.
        return await ReadJobAsync(job.JobId, cancellationToken);
    }

    private async Task<RouteResult> TryRouteJobInternalAsync(PrintJob job, CancellationToken cancellationToken)
    {
        var printerId = await _resolver.ResolvePrinterAsync(
            job.StoreId,
            job.DocumentType,
            job.Channel,
            cancellationToken);

        if (printerId is null)
        {
            await ApplyRouteNotFoundAsync(job, cancellationToken);
            return new RouteResult(false, null, RouteNotFoundCode);
        }

        var now = DateTimeOffset.UtcNow;
        var oldStatus = job.Status;

        var routed = await _db.PrintJobs
            .Where(x => x.JobId == job.JobId && x.Status == oldStatus)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, PrintJobStatus.Routed)
                .SetProperty(x => x.PrinterId, printerId)
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);

        if (routed != 1)
            throw new PrintJobStateConflictException(
                $"El job {job.JobId} cambio de estado mientras se enrutaba; vuelva a consultarlo.");

        var routedEvent = new PrintJobEvent
        {
            JobId = job.JobId,
            EventType = "ROUTED",
            OldStatus = oldStatus,
            NewStatus = PrintJobStatus.Routed,
            ActorType = "system",
            Message = $"Enrutado a impresora {printerId}.",
            OccurredAtUtc = now
        };

        _db.PrintJobEvents.Add(routedEvent);
        await _db.SaveChangesAsync(cancellationToken);

        return new RouteResult(true, printerId, null);
    }

    public async Task TryRouteBatchAsync(IReadOnlyList<Guid> jobIds, CancellationToken cancellationToken = default)
    {
        if (jobIds.Count == 0) return;

        var jobs = await _db.PrintJobs
            .Where(j => jobIds.Contains(j.JobId) && j.Status == PrintJobStatus.Pending)
            .ToListAsync(cancellationToken);

        if (jobs.Count == 0) return;

        var requests = jobs.Select(j => (j.StoreId, j.DocumentType, j.Channel)).ToList();
        var printerIds = await _resolver.ResolveBatchAsync(requests, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            var printerId = printerIds[i];

            if (printerId is null)
            {
                job.Status = PrintJobStatus.ErrorFinal;
                job.LastErrorCode = RouteNotFoundCode;
                job.LastErrorMessage = "No existe regla activa aplicable para este trabajo.";
                job.UpdatedAtUtc = now;
                _db.PrintJobEvents.Add(new PrintJobEvent
                {
                    JobId = job.JobId,
                    EventType = "ROUTE_NOT_FOUND",
                    OldStatus = PrintJobStatus.Pending,
                    NewStatus = PrintJobStatus.ErrorFinal,
                    ErrorCode = RouteNotFoundCode,
                    Message = "No existe regla activa aplicable para este trabajo.",
                    ActorType = "system",
                    OccurredAtUtc = now
                });
            }
            else
            {
                job.Status = PrintJobStatus.Routed;
                job.PrinterId = printerId;
                job.UpdatedAtUtc = now;
                _db.PrintJobEvents.Add(new PrintJobEvent
                {
                    JobId = job.JobId,
                    EventType = "ROUTED",
                    OldStatus = PrintJobStatus.Pending,
                    NewStatus = PrintJobStatus.Routed,
                    ActorType = "system",
                    Message = $"Enrutado a impresora {printerId}.",
                    OccurredAtUtc = now
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyRouteNotFoundAsync(PrintJob job, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var oldStatus = job.Status;

        var failed = await _db.PrintJobs
            .Where(x => x.JobId == job.JobId && x.Status == oldStatus)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, PrintJobStatus.ErrorFinal)
                .SetProperty(x => x.LastErrorCode, RouteNotFoundCode)
                .SetProperty(x => x.LastErrorMessage, "No existe regla activa aplicable para este trabajo.")
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);

        // Sin ruta y ademas ya no esta donde lo dejamos: no hay nada que marcar ni que registrar.
        if (failed != 1)
            return;

        var errorEvent = new PrintJobEvent
        {
            JobId = job.JobId,
            EventType = "ROUTE_NOT_FOUND",
            OldStatus = oldStatus,
            NewStatus = PrintJobStatus.ErrorFinal,
            ErrorCode = RouteNotFoundCode,
            Message = "No existe regla activa aplicable para este trabajo.",
            ActorType = "system",
            OccurredAtUtc = now
        };

        _db.PrintJobEvents.Add(errorEvent);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
