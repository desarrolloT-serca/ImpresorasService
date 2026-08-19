using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Infrastructure.Services;

public class RoutingService : IRoutingService
{
    private readonly ImpresorasDbContext _db;
    private readonly IRoutingResolver _resolver;

    public const string RouteNotFoundCode = "ROUTE_NOT_FOUND";

    public RoutingService(ImpresorasDbContext db, IRoutingResolver resolver)
    {
        _db = db;
        _resolver = resolver;
    }

    /// <summary>
    /// Reintento/reimpresion manual. Fase 2.5: las transiciones van por UPDATE condicionado al
    /// estado esperado, asi que si el Worker reclamo el trabajo entre la lectura y la escritura
    /// la operacion afecta a 0 filas y se responde con la verdad, en vez de confirmar un cambio
    /// que nunca llego a aplicarse.
    /// </summary>
    public async Task<RouteResult> TryRetryRouteAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _db.PrintJobs.AsNoTracking().FirstOrDefaultAsync(x => x.JobId == jobId, cancellationToken);
        if (job is null)
            throw new InvalidOperationException($"Job {jobId} no encontrado.");

        // PrintedUnknown entra aquí porque es la única salida de "reimprimir asumiendo el riesgo":
        // el sistema paró en vez de reenviar solo (puede haberse impreso ya), así que la reimpresión
        // es una decisión humana y queda registrada como tal en el evento.
        if (job.Status is PrintJobStatus.ErrorFinal or PrintJobStatus.PrintedUnknown)
        {
            var previousStatus = job.Status;
            var resetAt = DateTimeOffset.UtcNow;

            // Reintento manual: permitir volver a enrutar cualquier ErrorFinal
            // (ROUTE_NOT_FOUND, PRINTER_INVALID, RETRIES_EXHAUSTED, etc.).
            var reset = await _db.PrintJobs
                .Where(x => x.JobId == jobId && x.Status == previousStatus)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, PrintJobStatus.Pending)
                    .SetProperty(x => x.PrinterId, (int?)null)
                    .SetProperty(x => x.AttemptCount, 0)
                    .SetProperty(x => x.NextRetryAtUtc, (DateTimeOffset?)null)
                    .SetProperty(x => x.LastErrorCode, (string?)null)
                    .SetProperty(x => x.LastErrorMessage, (string?)null)
                    .SetProperty(x => x.UpdatedAtUtc, resetAt), cancellationToken);

            if (reset != 1)
                throw new PrintJobStateConflictException(
                    $"El job {jobId} cambio de estado mientras se preparaba el reintento; vuelva a consultarlo.");

            _db.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = jobId,
                EventType = "RETRY_ROUTE",
                OldStatus = previousStatus,
                NewStatus = PrintJobStatus.Pending,
                ActorType = "user",
                Message = previousStatus == PrintJobStatus.PrintedUnknown
                    ? "Reimpresión manual solicitada desde PrintedUnknown: el operador asume el riesgo de duplicado."
                    : "Reintento manual solicitado desde ErrorFinal.",
                OccurredAtUtc = resetAt
            });
            await _db.SaveChangesAsync(cancellationToken);

            // ExecuteUpdate no refresca lo ya leido: sin esto el enrutado seguiria viendo el
            // estado anterior y trataria de transicionar desde el.
            job = await _db.PrintJobs.AsNoTracking().FirstAsync(x => x.JobId == jobId, cancellationToken);
        }
        else if (job.Status != PrintJobStatus.Pending)
        {
            throw new InvalidOperationException(
                $"El job {jobId} no está en Pending, ErrorFinal ni PrintedUnknown (actual: {job.Status}).");
        }

        return await TryRouteJobInternalAsync(job, cancellationToken);
    }

    public async Task<RouteResult> TryRouteJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _db.PrintJobs.AsNoTracking().FirstOrDefaultAsync(x => x.JobId == jobId, cancellationToken);
        if (job is null)
            throw new InvalidOperationException($"Job {jobId} no encontrado.");

        if (job.Status != PrintJobStatus.Pending)
            throw new InvalidOperationException($"El job {jobId} no está en Pending (actual: {job.Status}).");

        return await TryRouteJobInternalAsync(job, cancellationToken);
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
