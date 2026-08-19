using System.Security.Claims;
using ImpresorasService.Api.Controllers;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Flow;

/// <summary>
/// Fase 2.5: operaciones manuales condicionadas al estado. Lo que cubre es una mentira concreta que
/// la API contaba: cancelabas un trabajo que el Worker acababa de reclamar, te respondia "Cancelled"
/// y el papel salia igual por la impresora.
/// </summary>
public sealed class PrintJobsControllerConflictTests
{
    /// <remarks>
    /// Aqui el Worker reclama ANTES de que el controlador lea, asi que este ve Printing y responde
    /// 400. La otra mitad -reclamar en la ventana entre la lectura y el UPDATE, que devuelve 409- no
    /// es reproducible sin interponerse en el comando SQL; lo que si queda fijado, y era el defecto,
    /// es que en ninguno de los dos casos se responde "Cancelled" ni se escribe el evento mientras
    /// el papel sale por la impresora.
    /// </remarks>
    [Fact]
    public async Task Cancel_WhenWorkerAlreadyClaimedTheJob_DoesNotCancelAndLeavesItPrinting()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;
        var jobId = await SeedAsync(db, PrintJobStatus.Routed);

        var controller = NewController(db, isAdmin: true);

        // El Worker reclama el trabajo entre que el operador abre la cola y pulsa "Cancelar".
        await db.PrintJobs
            .Where(j => j.JobId == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PrintJobStatus.Printing));

        var result = await controller.Cancel(jobId, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);

        db.ChangeTracker.Clear();
        var job = await db.PrintJobs.AsNoTracking().FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.Printing, job.Status);

        // Y no queda un evento de cancelacion que contradiga al estado.
        Assert.False(await db.PrintJobEvents.AnyAsync(e => e.JobId == jobId && e.EventType == "CANCELLED_BY_USER"));
    }

    [Fact]
    public async Task Cancel_WhenStateIsUnchanged_StillWorks()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;
        var jobId = await SeedAsync(db, PrintJobStatus.Routed);

        var result = await NewController(db, isAdmin: true).Cancel(jobId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);

        db.ChangeTracker.Clear();
        var job = await db.PrintJobs.AsNoTracking().FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.Cancelled, job.Status);
    }

    private static PrintJobsController NewController(ImpresorasDbContext db, bool isAdmin)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, isAdmin ? "Admin" : "StoreManager"),
            new("StoreId", "1"),
            new(ClaimTypes.NameIdentifier, "user-1"),
            new(ClaimTypes.Name, "user-1"),
        };

        return new PrintJobsController(db, new DummyRoutingService(), TimeProvider.System)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
                }
            }
        };
    }

    private static async Task<Guid> SeedAsync(ImpresorasDbContext db, PrintJobStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "TEST",
            ExternalJobId = "EXT-CONFLICT",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = status,
            PrinterId = 10,
            AttemptCount = 0,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return jobId;
    }
}
