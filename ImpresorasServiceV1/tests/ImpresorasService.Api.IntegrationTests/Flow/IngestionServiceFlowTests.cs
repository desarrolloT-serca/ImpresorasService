using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Options;
using ImpresorasService.Application.Services;
using ImpresorasService.Application.Models;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Adapters;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Repositories;
using ImpresorasService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Flow;

internal sealed class StaticJobSourceAdapter : IJobSourceAdapter
{
    private readonly IReadOnlyList<IncomingPrintJob> _jobs;
    private readonly ImpresorasDbContext _db;

    public StaticJobSourceAdapter(ImpresorasDbContext db, IReadOnlyList<IncomingPrintJob> jobs)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public Task<IReadOnlyList<IncomingPrintJob>> FetchPendingJobsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        var take = Math.Max(0, batchSize);
        var result = _jobs.Take(take).ToList();
        return Task.FromResult<IReadOnlyList<IncomingPrintJob>>(result);
    }

    public async Task MarkJobsProcessedAsync(
        IReadOnlyList<long> sourceJobIds,
        CancellationToken cancellationToken)
    {
        if (sourceJobIds is null || sourceJobIds.Count == 0)
            return;

        var idSet = sourceJobIds.ToHashSet();
        var rows = await _db.SourcePrintJobs
            .Where(x => idSet.Contains(x.Id))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.IsProcessed = true;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task RenewJobLeasesAsync(IReadOnlyList<long> sourceJobIds, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class IngestionServiceFlowTests
{
    [Fact]
    public async Task IngestBatchAsync_WhenSingleSourceJobMatchesRule_InsertsPendingRoutesAndMarksSourceProcessed()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var storeId = 1;
        var now = DateTimeOffset.UtcNow;

        // Arrange: printer + regla activa para resolver.
        var printer = new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            Host = null,
            StoreId = storeId,
            IsActive = true,
            CapabilitiesJson = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Printers.Add(printer);

        var rule = new RoutingRule
        {
            RuleId = 1,
            Priority = 1,
            StoreId = storeId,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PrinterId = printer.PrinterId,
            IsActive = true,
            ValidFromUtc = now.AddMinutes(-1),
            ValidToUtc = null,
            CreatedBy = "test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.RoutingRules.Add(rule);

        // Arrange: source job en SourcePrintJobs (IsProcessed=false).
        var sourceJobId = 100L;
        var sourceRecord = new SourcePrintJobRecord
        {
            Id = sourceJobId,
            SourceSystem = "SAP-TEST",
            ExternalJobId = "EXT-1",
            StoreId = storeId,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            CreatedAtUtc = now,
            IsProcessed = false
        };
        db.SourcePrintJobs.Add(sourceRecord);
        await db.SaveChangesAsync();

        var expectedSha = ComputeSha256Hex(sourceRecord.PdfBlob);

        var adapter = new StaticJobSourceAdapter(db, new[]
        {
            new IncomingPrintJob(
                SourceJobId: sourceJobId,
                SourceSystem: sourceRecord.SourceSystem,
                ExternalJobId: sourceRecord.ExternalJobId,
                StoreId: sourceRecord.StoreId,
                DocumentType: sourceRecord.DocumentType,
                Channel: sourceRecord.Channel ?? "DEFAULT",
                PdfBlob: sourceRecord.PdfBlob,
                CreatedAtUtc: sourceRecord.CreatedAtUtc)
        });

        var printJobRepository = new PrintJobRepository(db);
        var routingResolver = new RoutingResolver(db);
        var routingService = new RoutingService(db, routingResolver);

        var ingestion = new IngestionService(
            jobSourceAdapter: adapter,
            printJobRepository: printJobRepository,
            routingService: routingService,
            logger: NullLogger<IngestionService>.Instance);

        // Act
        var insertedCount = await ingestion.IngestBatchAsync(batchSize: 10, CancellationToken.None);

        // Assert: inserted + processed + routed.
        Assert.Equal(1, insertedCount);

        var job = await db.PrintJobs.SingleAsync(j =>
            j.SourceSystem == sourceRecord.SourceSystem && j.ExternalJobId == sourceRecord.ExternalJobId);

        Assert.Equal(PrintJobStatus.Routed, job.Status);
        Assert.Equal(printer.PrinterId, job.PrinterId);
        Assert.Equal(expectedSha, job.PdfSha256);

        var sourceAfter = await db.SourcePrintJobs.SingleAsync(s => s.Id == sourceJobId);
        Assert.True(sourceAfter.IsProcessed);

        var events = await db.PrintJobEvents
            .Where(e => e.JobId == job.JobId)
            .OrderBy(e => e.EventId)
            .ToListAsync();

        Assert.Contains(events, e => e.EventType == "INGESTED" && e.NewStatus == PrintJobStatus.Pending);
        Assert.Contains(events, e => e.EventType == "ROUTED" && e.NewStatus == PrintJobStatus.Routed);
    }

    [Fact]
    public async Task IngestBatchAsync_WhenDuplicateSourceExternalId_SkipsInsertButStillMarksSourceProcessed()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var storeId = 1;
        var now = DateTimeOffset.UtcNow;

        var sourceJobId = 101L;
        var sourceRecord = new SourcePrintJobRecord
        {
            Id = sourceJobId,
            SourceSystem = "SAP-TEST",
            ExternalJobId = "EXT-2",
            StoreId = storeId,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            CreatedAtUtc = now,
            IsProcessed = false
        };
        db.SourcePrintJobs.Add(sourceRecord);

        // Duplicate already exists in PrintJobs by (SourceSystem, ExternalJobId).
        db.PrintJobs.Add(new PrintJob
        {
            JobId = Guid.NewGuid(),
            SourceSystem = sourceRecord.SourceSystem,
            ExternalJobId = sourceRecord.ExternalJobId,
            StoreId = sourceRecord.StoreId,
            DocumentType = sourceRecord.DocumentType,
            Channel = sourceRecord.Channel ?? "DEFAULT",
            PdfBlob = sourceRecord.PdfBlob,
            PdfSha256 = "abc",
            Status = PrintJobStatus.Pending,
            PrinterId = null,
            AttemptCount = 0,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        });

        await db.SaveChangesAsync();

        var adapter = new StaticJobSourceAdapter(db, new[]
        {
            new IncomingPrintJob(
                SourceJobId: sourceJobId,
                SourceSystem: sourceRecord.SourceSystem,
                ExternalJobId: sourceRecord.ExternalJobId,
                StoreId: sourceRecord.StoreId,
                DocumentType: sourceRecord.DocumentType,
                Channel: sourceRecord.Channel ?? "DEFAULT",
                PdfBlob: sourceRecord.PdfBlob,
                CreatedAtUtc: sourceRecord.CreatedAtUtc)
        });

        // RoutingService shouldn't matter because insertion is skipped.
        var printJobRepository = new PrintJobRepository(db);
        var routingResolver = new RoutingResolver(db);
        var routingService = new RoutingService(db, routingResolver);

        var ingestion = new IngestionService(
            jobSourceAdapter: adapter,
            printJobRepository: printJobRepository,
            routingService: routingService,
            logger: NullLogger<IngestionService>.Instance);

        // Act
        var insertedCount = await ingestion.IngestBatchAsync(batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Equal(0, insertedCount);

        var sourceAfter = await db.SourcePrintJobs.SingleAsync(s => s.Id == sourceJobId);
        Assert.True(sourceAfter.IsProcessed);

        var jobs = await db.PrintJobs
            .Where(j => j.SourceSystem == sourceRecord.SourceSystem && j.ExternalJobId == sourceRecord.ExternalJobId)
            .ToListAsync();
        Assert.Single(jobs);
    }

    [Fact]
    public async Task IngestBatchAsync_WhenNoRoutingRule_TransitionsToErrorFinalWithRouteNotFound()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var now = DateTimeOffset.UtcNow;

        // Arrange: source job (sin reglas activas).
        var sourceJobId = 102L;
        var sourceRecord = new SourcePrintJobRecord
        {
            Id = sourceJobId,
            SourceSystem = "SAP-TEST",
            ExternalJobId = "EXT-3",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            CreatedAtUtc = now,
            IsProcessed = false
        };
        db.SourcePrintJobs.Add(sourceRecord);
        await db.SaveChangesAsync();

        var adapter = new StaticJobSourceAdapter(db, new[]
        {
            new IncomingPrintJob(
                SourceJobId: sourceJobId,
                SourceSystem: sourceRecord.SourceSystem,
                ExternalJobId: sourceRecord.ExternalJobId,
                StoreId: sourceRecord.StoreId,
                DocumentType: sourceRecord.DocumentType,
                Channel: sourceRecord.Channel ?? "DEFAULT",
                PdfBlob: sourceRecord.PdfBlob,
                CreatedAtUtc: sourceRecord.CreatedAtUtc)
        });

        var printJobRepository = new PrintJobRepository(db);
        var routingResolver = new RoutingResolver(db);
        var routingService = new RoutingService(db, routingResolver);

        var ingestion = new IngestionService(
            jobSourceAdapter: adapter,
            printJobRepository: printJobRepository,
            routingService: routingService,
            logger: NullLogger<IngestionService>.Instance);

        // Act
        var insertedCount = await ingestion.IngestBatchAsync(batchSize: 10, CancellationToken.None);

        // Assert
        Assert.Equal(1, insertedCount);

        var job = await db.PrintJobs.SingleAsync(j =>
            j.SourceSystem == sourceRecord.SourceSystem && j.ExternalJobId == sourceRecord.ExternalJobId);

        Assert.Equal(PrintJobStatus.ErrorFinal, job.Status);
        Assert.Equal(RoutingService.RouteNotFoundCode, job.LastErrorCode);

        var sourceAfter = await db.SourcePrintJobs.SingleAsync(s => s.Id == sourceJobId);
        Assert.True(sourceAfter.IsProcessed);
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

