using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/[controller]")]
public class SourcePrintJobsController : ControllerBase
{
    private readonly ImpresorasDbContext _dbContext;

    public SourcePrintJobsController(ImpresorasDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpPost("test")]
    public async Task<IActionResult> CreateTestJob(
        [FromBody] CreateSourcePrintJobRequest request,
        CancellationToken cancellationToken)
    {
        string sourceSystem = string.IsNullOrWhiteSpace(request.SourceSystem) ? "SAP-TEST" : request.SourceSystem;
        string externalJobId = string.IsNullOrWhiteSpace(request.ExternalJobId)
            ? $"JOB-{Guid.NewGuid():N}"
            : request.ExternalJobId;

        bool existsInSource = await _dbContext.SourcePrintJobs
            .AsNoTracking()
            .AnyAsync(
                x => x.SourceSystem == sourceSystem && x.ExternalJobId == externalJobId,
                cancellationToken);

        bool existsInQueue = await _dbContext.PrintJobs
            .AsNoTracking()
            .AnyAsync(
                x => x.SourceSystem == sourceSystem && x.ExternalJobId == externalJobId,
                cancellationToken);

        if (existsInSource || existsInQueue)
        {
            return Conflict(new
            {
                message = "Trabajo duplicado: SourceSystem + ExternalJobId ya existe.",
                sourceSystem,
                externalJobId
            });
        }

        var row = new SourcePrintJobRecord
        {
            SourceSystem = sourceSystem,
            ExternalJobId = externalJobId,
            StoreId = request.StoreId,
            DocumentType = string.IsNullOrWhiteSpace(request.DocumentType) ? "TICKET" : request.DocumentType,
            Channel = string.IsNullOrWhiteSpace(request.Channel) ? "DEFAULT" : request.Channel,
            PdfBlob = request.PdfBlob?.Length > 0 ? request.PdfBlob : MinimalPdf.Bytes,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsProcessed = false
        };

        _dbContext.SourcePrintJobs.Add(row);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            row.Id,
            row.ExternalJobId,
            row.StoreId,
            row.CreatedAtUtc
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetPending(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SourcePrintJobs
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Take(100)
            .Select(x => new
            {
                x.Id,
                x.SourceSystem,
                x.ExternalJobId,
                x.StoreId,
                x.DocumentType,
                x.Channel,
                x.IsProcessed,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }
}

public class CreateSourcePrintJobRequest
{
    public string? SourceSystem { get; set; }
    public string? ExternalJobId { get; set; }
    public int StoreId { get; set; } = 101;
    public string? DocumentType { get; set; }
    public string? Channel { get; set; }
    public byte[]? PdfBlob { get; set; }
}
