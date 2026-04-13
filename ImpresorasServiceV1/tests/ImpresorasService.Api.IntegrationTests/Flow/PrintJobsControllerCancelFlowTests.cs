using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
using ImpresorasService.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Flow;

internal sealed class DummyRoutingService : IRoutingService
{
    public Task<RouteResult> TryRetryRouteAsync(Guid jobId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<RouteResult> TryRouteJobAsync(Guid jobId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

public sealed class PrintJobsControllerCancelFlowTests
{
    [Fact]
    public async Task Cancel_WhenPending_Admin_ReturnsOkAndPersistsCancelledEvent()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "SAP-TEST",
            ExternalJobId = "EXT-1",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Pending,
            PrinterId = null,
            AttemptCount = 0,
            NextRetryAtUtc = null,
            LastErrorCode = null,
            LastErrorMessage = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        });
        await db.SaveChangesAsync();

        var controller = new PrintJobsController(db, new DummyRoutingService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "Admin", storeId: "1")
            }
        };

        // Act
        var result = await controller.Cancel(jobId, CancellationToken.None);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.Cancelled, jobAfter.Status);
        Assert.Null(jobAfter.NextRetryAtUtc);

        var lastEvent = await db.PrintJobEvents
            .Where(e => e.JobId == jobId)
            .OrderByDescending(e => e.EventId)
            .FirstOrDefaultAsync();

        Assert.NotNull(lastEvent);
        Assert.Equal("CANCELLED_BY_USER", lastEvent!.EventType);
        Assert.Equal(PrintJobStatus.Pending, lastEvent.OldStatus);
        Assert.Equal(PrintJobStatus.Cancelled, lastEvent.NewStatus);
        Assert.Equal("user-1", lastEvent.ActorId);
    }

    [Fact]
    public async Task Cancel_WhenPrinting_Admin_ReturnsBadRequestAndDoesNotChangeStatus()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "SAP-TEST",
            ExternalJobId = "EXT-2",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Printing,
            PrinterId = 10,
            AttemptCount = 2,
            NextRetryAtUtc = null,
            LastErrorCode = "X",
            LastErrorMessage = "Y",
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        });
        await db.SaveChangesAsync();

        var controller = new PrintJobsController(db, new DummyRoutingService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "Admin", storeId: "1")
            }
        };

        var result = await controller.Cancel(jobId, CancellationToken.None);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(bad.Value);

        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.Printing, jobAfter.Status);
    }

    [Fact]
    public async Task Cancel_WhenErrorFinal_Admin_ReturnsOkAndPersistsCancelledEvent()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "SAP-TEST",
            ExternalJobId = "EXT-3",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.ErrorFinal,
            PrinterId = null,
            AttemptCount = 4,
            NextRetryAtUtc = null,
            LastErrorCode = "ROUTE_NOT_FOUND",
            LastErrorMessage = "No hay regla",
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        });
        await db.SaveChangesAsync();

        var controller = new PrintJobsController(db, new DummyRoutingService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "Admin", storeId: "1")
            }
        };

        var result = await controller.Cancel(jobId, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.Cancelled, jobAfter.Status);

        var eventType = await db.PrintJobEvents
            .Where(e => e.JobId == jobId)
            .Select(e => e.EventType)
            .ToListAsync();

        Assert.Contains("CANCELLED_BY_USER", eventType);
    }

    [Fact]
    public async Task Cancel_WhenStoreManagerAndJobNotInStore_ReturnsForbid()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "SAP-TEST",
            ExternalJobId = "EXT-4",
            StoreId = 2,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Pending,
            PrinterId = null,
            AttemptCount = 0,
            NextRetryAtUtc = null,
            LastErrorCode = null,
            LastErrorMessage = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        });
        await db.SaveChangesAsync();

        var controller = new PrintJobsController(db, new DummyRoutingService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "StoreManager", storeId: "1")
            }
        };

        var result = await controller.Cancel(jobId, CancellationToken.None);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Cancel_WhenStoreManagerAndJobInStore_ReturnsOkAndPersistsCancelledEvent()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "SAP-TEST",
            ExternalJobId = "EXT-5",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Routed,
            PrinterId = 10,
            AttemptCount = 1,
            NextRetryAtUtc = null,
            LastErrorCode = null,
            LastErrorMessage = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        });
        await db.SaveChangesAsync();

        var controller = new PrintJobsController(db, new DummyRoutingService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "StoreManager", storeId: "1")
            }
        };

        // Act
        var result = await controller.Cancel(jobId, CancellationToken.None);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.Cancelled, jobAfter.Status);

        var lastEvent = await db.PrintJobEvents
            .Where(e => e.JobId == jobId)
            .OrderByDescending(e => e.EventId)
            .FirstOrDefaultAsync();

        Assert.NotNull(lastEvent);
        Assert.Equal("CANCELLED_BY_USER", lastEvent!.EventType);
        Assert.Equal("user-1", lastEvent.ActorId);
        Assert.Equal(PrintJobStatus.Routed, lastEvent.OldStatus);
    }

    [Fact]
    public async Task Cancel_WhenSpoolAccepted_ReturnsBadRequestAndDoesNotChangeStatus()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "SAP-TEST",
            ExternalJobId = "EXT-6",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.SpoolAccepted,
            PrinterId = 10,
            AttemptCount = 1,
            NextRetryAtUtc = null,
            LastErrorCode = null,
            LastErrorMessage = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        });
        await db.SaveChangesAsync();

        var controller = new PrintJobsController(db, new DummyRoutingService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = BuildPrincipal(role: "Admin", storeId: "1")
            }
        };

        var result = await controller.Cancel(jobId, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);

        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.SpoolAccepted, jobAfter.Status);
    }

    private static ClaimsPrincipal BuildPrincipal(string role, string storeId)
    {
        // Nota de decisión: PrintJobsController usa User.IsInRole("...") y lee claim "StoreId".
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, role),
            new("StoreId", storeId),
            new(ClaimTypes.NameIdentifier, "user-1"),
            new(ClaimTypes.Name, "user-1"),
        };

        var identity = new ClaimsIdentity(claims, authenticationType: "test");
        return new ClaimsPrincipal(identity);
    }
}

