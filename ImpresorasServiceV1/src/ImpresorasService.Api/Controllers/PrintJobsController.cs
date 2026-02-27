using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PrintJobsController : ControllerBase
{
    private readonly ImpresorasDbContext _dbContext;
    private readonly IRoutingService _routingService;

    public PrintJobsController(ImpresorasDbContext dbContext, IRoutingService routingService)
    {
        _dbContext = dbContext;
        _routingService = routingService;
    }

    [HttpGet]
    public async Task<IActionResult> GetQueue(
        [FromQuery] int? storeId,
        [FromQuery] PrintJobStatus? status,
        CancellationToken cancellationToken)
    {
        IQueryable<PrintJob> query = _dbContext.PrintJobs.AsNoTracking();

        if (storeId.HasValue)
        {
            query = query.Where(x => x.StoreId == storeId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        var results = (await query
            .Select(x => new
            {
                x.JobId,
                x.ExternalJobId,
                x.StoreId,
                x.DocumentType,
                x.Status,
                x.AttemptCount,
                x.LastErrorCode,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .ToList();

        return Ok(results);
    }

    /// <summary>
    /// Intenta enrutar un trabajo. Si no hay regla aplicable, transiciona a ErrorFinal con ROUTE_NOT_FOUND.
    /// </summary>
    [HttpPost("{id:guid}/route")]
    public async Task<IActionResult> Route(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _routingService.TryRouteJobAsync(id, cancellationToken);
            if (result.Success)
                return Ok(new { status = "Routed", printerId = result.PrinterId });
            return Ok(new { status = "ErrorFinal", errorCode = result.ErrorCode });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
