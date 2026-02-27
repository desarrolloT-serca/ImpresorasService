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

    public async Task<RouteResult> TryRouteJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _db.PrintJobs.FirstOrDefaultAsync(x => x.JobId == jobId, cancellationToken);
        if (job is null)
            throw new InvalidOperationException($"Job {jobId} no encontrado.");

        if (job.Status != PrintJobStatus.Pending)
            throw new InvalidOperationException($"El job {jobId} no está en Pending (actual: {job.Status}).");

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
        job.Status = PrintJobStatus.Routed;
        job.PrinterId = printerId;
        job.UpdatedAtUtc = now;

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

    private async Task ApplyRouteNotFoundAsync(PrintJob job, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var oldStatus = job.Status;

        job.Status = PrintJobStatus.ErrorFinal;
        job.LastErrorCode = RouteNotFoundCode;
        job.LastErrorMessage = "No existe regla activa aplicable para este trabajo.";
        job.UpdatedAtUtc = now;

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
