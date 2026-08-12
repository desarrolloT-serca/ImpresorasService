using System;
using System.Threading;
using System.Threading.Tasks;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Flow;

public sealed class RoutingServiceFlowTests
{
    [Fact]
    public async Task TryRetryRouteAsync_WhenJobInErrorFinalAndRuleExists_ResetsAndRoutesToPrinter()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;

        db.Stores.Add(new Store
        {
            StoreId = 1,
            Name = "Store 1",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        db.Printers.Add(new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            Host = null,
            StoreId = 1,
            IsActive = true,
            CapabilitiesJson = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        db.RoutingRules.Add(new RoutingRule
        {
            RuleId = 1,
            Priority = 1,
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PrinterId = 10,
            IsActive = true,
            ValidFromUtc = now.AddMinutes(-1),
            ValidToUtc = null,
            CreatedBy = "test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

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
            Status = PrintJobStatus.ErrorFinal,
            PrinterId = null,
            AttemptCount = 3,
            NextRetryAtUtc = null,
            LastErrorCode = "ROUTE_NOT_FOUND",
            LastErrorMessage = "No hay regla",
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await db.SaveChangesAsync();

        var resolver = new RoutingResolver(db);
        var service = new RoutingService(db, resolver);

        // Act
        var result = await service.TryRetryRouteAsync(jobId, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(10, result.PrinterId);

        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.Routed, jobAfter.Status);
        Assert.Equal(10, jobAfter.PrinterId);
        Assert.Equal(0, jobAfter.AttemptCount);
        Assert.Null(jobAfter.LastErrorCode);
        Assert.Null(jobAfter.LastErrorMessage);
        Assert.Null(jobAfter.NextRetryAtUtc);

        var events = await db.PrintJobEvents
            .Where(e => e.JobId == jobId)
            .OrderBy(e => e.EventId)
            .ToListAsync();

        Assert.Contains(events, e => e.EventType == "RETRY_ROUTE" && e.NewStatus == PrintJobStatus.Pending);
        Assert.Contains(events, e => e.EventType == "ROUTED" && e.NewStatus == PrintJobStatus.Routed);
    }

    [Fact]
    public async Task TryRetryRouteAsync_WhenRuleHasChannelButNoDocumentType_MatchesAnyDocumentType()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;

        db.Stores.Add(new Store
        {
            StoreId = 1,
            Name = "Store 1",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        db.Printers.Add(new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            Host = null,
            StoreId = 1,
            IsActive = true,
            CapabilitiesJson = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        // Regla "cualquier tipo de documento" para un canal concreto: DocumentType null, Channel fijo.
        db.RoutingRules.Add(new RoutingRule
        {
            RuleId = 1,
            Priority = 1,
            StoreId = 1,
            DocumentType = null,
            Channel = "DEFAULT",
            PrinterId = 10,
            IsActive = true,
            ValidFromUtc = now.AddMinutes(-1),
            ValidToUtc = null,
            CreatedBy = "test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var jobId = Guid.NewGuid();
        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "SAP-TEST",
            ExternalJobId = "EXT-4",
            StoreId = 1,
            DocumentType = "TIPO_INVENTADO",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.ErrorFinal,
            PrinterId = null,
            AttemptCount = 3,
            NextRetryAtUtc = null,
            LastErrorCode = "ROUTE_NOT_FOUND",
            LastErrorMessage = "No hay regla",
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await db.SaveChangesAsync();

        var resolver = new RoutingResolver(db);
        var service = new RoutingService(db, resolver);

        // Act
        var result = await service.TryRetryRouteAsync(jobId, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(10, result.PrinterId);
    }

    [Fact]
    public async Task TryRetryRouteAsync_WhenJobInErrorFinalAndNoRuleExists_TransitionsToErrorFinalWithRouteNotFound()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;

        db.Stores.Add(new Store
        {
            StoreId = 1,
            Name = "Store 1",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        db.Printers.Add(new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            Host = null,
            StoreId = 1,
            IsActive = true,
            CapabilitiesJson = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

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
            Status = PrintJobStatus.ErrorFinal,
            PrinterId = null,
            AttemptCount = 2,
            NextRetryAtUtc = null,
            LastErrorCode = "ROUTE_NOT_FOUND",
            LastErrorMessage = "No hay regla",
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await db.SaveChangesAsync();

        var resolver = new RoutingResolver(db);
        var service = new RoutingService(db, resolver);

        // Act
        var result = await service.TryRetryRouteAsync(jobId, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.PrinterId);
        Assert.Equal(RoutingService.RouteNotFoundCode, result.ErrorCode);

        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.ErrorFinal, jobAfter.Status);
        Assert.Null(jobAfter.PrinterId);
        Assert.Equal(0, jobAfter.AttemptCount);
        Assert.Equal(RoutingService.RouteNotFoundCode, jobAfter.LastErrorCode);
    }

    [Fact]
    public async Task TryRetryRouteAsync_WhenJobNotInErrorFinal_ThrowsInvalidOperationException()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;

        db.Stores.Add(new Store
        {
            StoreId = 1,
            Name = "Store 1",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

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
            Status = PrintJobStatus.Pending,
            PrinterId = null,
            AttemptCount = 0,
            NextRetryAtUtc = null,
            LastErrorCode = null,
            LastErrorMessage = null,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await db.SaveChangesAsync();

        var resolver = new RoutingResolver(db);
        var service = new RoutingService(db, resolver);

        // Pending is allowed by contract; use a disallowed status instead.
        var job = await db.PrintJobs.FirstAsync(j => j.JobId == jobId);
        job.Status = PrintJobStatus.SpoolAccepted;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TryRetryRouteAsync(jobId, CancellationToken.None));
    }
}

