using System.Security.Claims;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Authorize(Policy = "EmployeeOrAbove")]
[Route("api/[controller]")]
public class PrintJobsController : ControllerBase
{
    private readonly ImpresorasDbContext _dbContext;
    private readonly IRoutingService _routingService;
    private readonly TimeProvider _timeProvider;

    public PrintJobsController(ImpresorasDbContext dbContext, IRoutingService routingService, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _routingService = routingService;
        _timeProvider = timeProvider;
    }

    [HttpGet]
    public async Task<IActionResult> GetQueue(
        [FromQuery] int? storeId,
        [FromQuery] PrintJobStatus? status,
        [FromQuery] int? limit,
        [FromQuery] int? page,
        [FromQuery] string? externalJobId,
        [FromQuery] bool? includeTotal,
        [FromQuery] bool? failedWithoutRetry,
        CancellationToken cancellationToken)
    {
        IQueryable<PrintJob> query = _dbContext.PrintJobs.AsNoTracking();

        var effectiveStoreId = IsAdmin() ? storeId : GetCurrentUserStoreId();
        if (!IsAdmin() && !effectiveStoreId.HasValue)
            return Forbid();
        if (effectiveStoreId.HasValue)
            query = query.Where(x => x.StoreId == effectiveStoreId.Value);

        // Mismo predicado que el KPI "sin reenviar" del dashboard, no una copia: así el número que
        // muestra el dashboard siempre se puede auditar listando exactamente esos trabajos. Con el
        // filtro por Status suelto no coincidían (un ErrorFinal es solo parte del conjunto).
        if (failedWithoutRetry == true)
        {
            query = query.Where(DashboardPrintJobPredicates.FailedWithoutRetryCurrent);
        }
        else if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(externalJobId))
        {
            var needle = externalJobId.Trim();
            query = query.Where(x => EF.Functions.Like(x.ExternalJobId, $"%{needle}%"));
        }

        // Protege la UI de cargas masivas accidentales (polling + tablas grandes).
        var safeLimit = Math.Clamp(limit ?? 100, 1, 500);
        var safePage = Math.Clamp(page ?? 1, 1, int.MaxValue / safeLimit); // evita overflow en Skip

        var projectedQuery =
            from job in query
            join printer in _dbContext.Printers.AsNoTracking()
                on job.PrinterId equals (int?)printer.PrinterId into printerJoin
            from printer in printerJoin.DefaultIfEmpty()
            orderby job.CreatedAtUtc descending
            select new
            {
                job.JobId,
                job.ExternalJobId,
                job.StoreId,
                job.PrinterId,
                PrinterName = printer == null ? null : printer.PrinterName,
                job.DocumentType,
                job.Status,
                job.AttemptCount,
                job.LastErrorCode,
                job.LastErrorMessage,
                job.CreatedAtUtc,
                job.UpdatedAtUtc
            };

        if (includeTotal == true)
        {
            var total = await query.CountAsync(cancellationToken);
            var pagedResults = await projectedQuery
                .Skip((safePage - 1) * safeLimit)
                .Take(safeLimit)
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                Value = pagedResults,
                Count = total,
                Page = safePage,
                PageSize = safeLimit
            });
        }

        var results = (await projectedQuery
            .Take(safeLimit)
            .ToListAsync(cancellationToken))
            .Cast<object>()
            .ToList();

        return Ok(results);
    }

    /// <summary>
    /// Intenta enrutar un trabajo. Acepta Pending o ErrorFinal.
    /// Si no hay regla aplicable, transiciona a ErrorFinal con ROUTE_NOT_FOUND.
    /// </summary>
    [HttpPost("{id:guid}/route")]
    [Authorize(Policy = "StoreManagerOrAdmin")]
    public async Task<IActionResult> Route(Guid id, CancellationToken cancellationToken)
    {
        if (!IsAdmin())
        {
            var userStoreId = GetCurrentUserStoreId();
            if (!userStoreId.HasValue)
                return Forbid();

            var jobStoreId = await _dbContext.PrintJobs
                .AsNoTracking()
                .Where(j => j.JobId == id)
                .Select(j => (int?)j.StoreId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!jobStoreId.HasValue)
                return NotFound();
            if (jobStoreId.Value != userStoreId.Value)
                return Forbid();
        }

        try
        {
            var result = await _routingService.TryRetryRouteAsync(id, cancellationToken);
            if (result.Success)
                return Ok(new { status = "Routed", printerId = result.PrinterId });
            return Ok(new { status = "ErrorFinal", errorCode = result.ErrorCode });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Cancela manualmente un trabajo para retirarlo del flujo activo.
    /// Permitido para StoreManager/Admin en su ambito de tienda.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "StoreManagerOrAdmin")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var job = await _dbContext.PrintJobs.FirstOrDefaultAsync(j => j.JobId == id, cancellationToken);
        if (job is null)
            return NotFound();

        if (!IsAdmin())
        {
            var userStoreId = GetCurrentUserStoreId();
            if (!userStoreId.HasValue || job.StoreId != userStoreId.Value)
                return Forbid();
        }

        var cancellableStates = new[]
        {
            PrintJobStatus.Pending,
            PrintJobStatus.Routed,
            PrintJobStatus.RetryScheduled,
            PrintJobStatus.ErrorFinal,
            // Estados de incertidumbre: el watchdog los produce de forma rutinaria (una impresora
            // sin IPP manda ahí todos sus trabajos) y sin esto quedaban sin ninguna acción posible
            // desde la interfaz — la única salida era un UPDATE manual en HANA.
            PrintJobStatus.PrintedUnknown,
            PrintJobStatus.PrinterBlocked
        };

        if (!cancellableStates.Contains(job.Status))
        {
            return BadRequest(new
            {
                error = $"El job {id} no puede cancelarse en estado {job.Status}."
            });
        }

        var now = _timeProvider.GetUtcNow();
        var oldStatus = job.Status;
        job.Status = PrintJobStatus.Cancelled;
        job.NextRetryAtUtc = null;
        job.UpdatedAtUtc = now;

        _dbContext.PrintJobEvents.Add(new PrintJobEvent
        {
            JobId = job.JobId,
            EventType = "CANCELLED_BY_USER",
            OldStatus = oldStatus,
            NewStatus = PrintJobStatus.Cancelled,
            ActorType = "user",
            ActorId = User.Identity?.Name ?? User.FindFirstValue("Login") ?? User.FindFirstValue(ClaimTypes.NameIdentifier),
            Message = "Trabajo cancelado manualmente desde interfaz operativa.",
            OccurredAtUtc = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { status = "Cancelled" });
    }

    /// <summary>
    /// Cierra manualmente un trabajo cuyo resultado el sistema no puede determinar por sí mismo,
    /// cuando un operador ha comprobado físicamente que el documento salió.
    /// Solo desde PrintedUnknown/PrinterBlocked: es una afirmación humana, no una deducción, y
    /// queda registrada como tal en el evento (ActorType="user").
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = "StoreManagerOrAdmin")]
    public async Task<IActionResult> ConfirmPrinted(Guid id, CancellationToken cancellationToken)
    {
        var job = await _dbContext.PrintJobs.FirstOrDefaultAsync(j => j.JobId == id, cancellationToken);
        if (job is null)
            return NotFound();

        if (!IsAdmin())
        {
            var userStoreId = GetCurrentUserStoreId();
            if (!userStoreId.HasValue || job.StoreId != userStoreId.Value)
                return Forbid();
        }

        if (job.Status is not (PrintJobStatus.PrintedUnknown or PrintJobStatus.PrinterBlocked))
        {
            return BadRequest(new
            {
                error = $"El job {id} no puede confirmarse manualmente en estado {job.Status}. " +
                        "Solo se confirman trabajos en estado desconocido o con la impresora bloqueada."
            });
        }

        var now = _timeProvider.GetUtcNow();
        var oldStatus = job.Status;
        job.Status = PrintJobStatus.PrintedConfirmed;
        job.LastErrorCode = null;
        job.LastErrorMessage = null;
        job.NextRetryAtUtc = null;
        job.UpdatedAtUtc = now;

        _dbContext.PrintJobEvents.Add(new PrintJobEvent
        {
            JobId = job.JobId,
            EventType = "CONFIRMED_BY_USER",
            OldStatus = oldStatus,
            NewStatus = PrintJobStatus.PrintedConfirmed,
            ActorType = "user",
            ActorId = User.Identity?.Name ?? User.FindFirstValue("Login") ?? User.FindFirstValue(ClaimTypes.NameIdentifier),
            Message = "Impresión confirmada manualmente por un operador tras comprobación física.",
            OccurredAtUtc = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { status = "PrintedConfirmed" });
    }

    private bool IsAdmin() => User.IsInRole("Admin");

    private int? GetCurrentUserStoreId()
    {
        var claimValue = User.FindFirstValue("StoreId");
        return int.TryParse(claimValue, out var storeId) ? storeId : null;
    }
}
