using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Options;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Flow;

public sealed class PrintExecutionServiceFlowTests
{
    [Fact]
    public async Task ExecuteBatchAsync_WhenRoutedJobAndSpoolerSuccess_TransitionsToSpoolAcceptedAndPersistsEvents()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var printer = new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            StoreId = 1,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Printers.Add(printer);

        var now = DateTimeOffset.UtcNow;
        var job = new PrintJob
        {
            JobId = Guid.NewGuid(),
            SourceSystem = "TEST",
            ExternalJobId = "EXT-1",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Routed,
            PrinterId = printer.PrinterId,
            AttemptCount = 0,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();

        var spooler = new FakeSpooler((_, __, ___) =>
            Task.FromResult(new PrintSpoolResult(true, null, null, false)));

        var options = Options.Create(new PrintExecutionOptions
        {
            PollIntervalSeconds = 0,
            BatchSize = 1,
            TimeoutSeconds = 1,
            MaxAttempts = 4,
            BackoffSeconds = [1, 2, 3, 4],
            PrintSettings = "fit",
            KeepTempFileOnFailure = false
        });

        var service = new PrintExecutionService(
            db,
            spooler,
            NullLogger<PrintExecutionService>.Instance,
            options,
            new DummyRoutingResolver(),
            TimeProvider.System);

        var processed = await service.ExecuteBatchAsync(batchSize: 1, CancellationToken.None);
        Assert.Equal(1, processed);
        Assert.Equal(1, spooler.CallCount);

        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == job.JobId);
        Assert.Equal(PrintJobStatus.SpoolAccepted, jobAfter.Status);
        Assert.Equal(1, jobAfter.AttemptCount);
        Assert.Null(jobAfter.NextRetryAtUtc);
        Assert.Null(jobAfter.LastErrorCode);
        Assert.Null(jobAfter.LastErrorMessage);
        Assert.Equal(MinimalPdf.Bytes, jobAfter.PdfBlob);

        var events = await db.PrintJobEvents.Where(e => e.JobId == job.JobId).OrderBy(e => e.EventId).ToListAsync();
        Assert.True(events.Count >= 2);
        Assert.Equal("StatusChanged", events[0].EventType);
        Assert.Equal(PrintJobStatus.Printing, events[0].NewStatus);
        Assert.Equal("StatusChanged", events[1].EventType);
        Assert.Equal(PrintJobStatus.SpoolAccepted, events[1].NewStatus);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WhenSpoolerTransientFailureAndAttemptsLeft_TransitionsToRetryScheduledWithBackoff()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var printer = new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            StoreId = 1,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Printers.Add(printer);

        var optionsModel = new PrintExecutionOptions
        {
            PollIntervalSeconds = 0,
            BatchSize = 1,
            TimeoutSeconds = 1,
            MaxAttempts = 4,
            BackoffSeconds = [15, 30, 60, 90],
            PrintSettings = "fit",
            KeepTempFileOnFailure = false
        };

        var now = DateTimeOffset.UtcNow;
        var job = new PrintJob
        {
            JobId = Guid.NewGuid(),
            SourceSystem = "TEST",
            ExternalJobId = "EXT-2",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Routed,
            PrinterId = printer.PrinterId,
            AttemptCount = 0,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();

        var spooler = new FakeSpooler((_, __, ___) =>
            Task.FromResult(new PrintSpoolResult(
                Success: false,
                ErrorCode: "SPOOLER_DOWN",
                ErrorMessage: "transient",
                IsTransient: true)));

        var options = Options.Create(optionsModel);
        var service = new PrintExecutionService(
            db,
            spooler,
            NullLogger<PrintExecutionService>.Instance,
            options,
            new DummyRoutingResolver(),
            TimeProvider.System);

        var before = DateTimeOffset.UtcNow;
        var processed = await service.ExecuteBatchAsync(batchSize: 1, CancellationToken.None);
        Assert.Equal(1, processed);

        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == job.JobId);
        Assert.Equal(PrintJobStatus.RetryScheduled, jobAfter.Status);
        Assert.Equal(1, jobAfter.AttemptCount);
        Assert.NotNull(jobAfter.NextRetryAtUtc);
        Assert.Equal("SPOOLER_DOWN", jobAfter.LastErrorCode);
        Assert.Equal("transient", jobAfter.LastErrorMessage);

        var expectedDelay = optionsModel.BackoffSeconds[0];
        Assert.True(jobAfter.NextRetryAtUtc!.Value >= before.AddSeconds(expectedDelay - 1));
        Assert.True(jobAfter.NextRetryAtUtc!.Value <= before.AddSeconds(expectedDelay + 2));
    }

    [Fact]
    public async Task ExecuteBatchAsync_WhenSpoolerPermanentFailure_TransitionsToErrorFinal()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var printer = new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            StoreId = 1,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Printers.Add(printer);

        var now = DateTimeOffset.UtcNow;
        var job = new PrintJob
        {
            JobId = Guid.NewGuid(),
            SourceSystem = "TEST",
            ExternalJobId = "EXT-3",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Routed,
            PrinterId = printer.PrinterId,
            AttemptCount = 0,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();

        var spooler = new FakeSpooler((_, __, ___) =>
            Task.FromResult(new PrintSpoolResult(
                Success: false,
                ErrorCode: "SPOOLER_DOWN",
                ErrorMessage: "permanent",
                IsTransient: false)));

        var options = Options.Create(new PrintExecutionOptions
        {
            TimeoutSeconds = 1,
            MaxAttempts = 4,
            BackoffSeconds = [1, 2, 3, 4],
            PrintSettings = "fit"
        });

        var service = new PrintExecutionService(
            db,
            spooler,
            NullLogger<PrintExecutionService>.Instance,
            options,
            new DummyRoutingResolver(),
            TimeProvider.System);
        var processed = await service.ExecuteBatchAsync(batchSize: 1, CancellationToken.None);
        Assert.Equal(1, processed);

        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == job.JobId);
        Assert.Equal(PrintJobStatus.ErrorFinal, jobAfter.Status);
        Assert.Null(jobAfter.NextRetryAtUtc);
        Assert.Equal("SPOOLER_DOWN", jobAfter.LastErrorCode);
        Assert.Equal("permanent", jobAfter.LastErrorMessage);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WhenPrinterIsInactive_TransitionsToErrorFinalWithPrinterInvalid()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var printer = new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            StoreId = 1,
            IsActive = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Printers.Add(printer);

        var now = DateTimeOffset.UtcNow;
        var job = new PrintJob
        {
            JobId = Guid.NewGuid(),
            SourceSystem = "TEST",
            ExternalJobId = "EXT-4",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Routed,
            PrinterId = printer.PrinterId,
            AttemptCount = 0,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();

        var spooler = new FakeSpooler((_, __, ___) =>
            Task.FromResult(new PrintSpoolResult(true, null, null, false)));

        var options = Options.Create(new PrintExecutionOptions
        {
            TimeoutSeconds = 1,
            MaxAttempts = 4,
            BackoffSeconds = [1, 2, 3, 4],
            PrintSettings = "fit"
        });

        var service = new PrintExecutionService(
            db,
            spooler,
            NullLogger<PrintExecutionService>.Instance,
            options,
            new DummyRoutingResolver(),
            TimeProvider.System);
        var processed = await service.ExecuteBatchAsync(batchSize: 1, CancellationToken.None);
        Assert.Equal(1, processed);

        Assert.Equal(0, spooler.CallCount);

        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == job.JobId);
        Assert.Equal(PrintJobStatus.ErrorFinal, jobAfter.Status);
        Assert.Equal("PRINTER_INVALID", jobAfter.LastErrorCode);
        Assert.Contains("Impresora inactiva", jobAfter.LastErrorMessage ?? string.Empty);
        Assert.Equal(0, jobAfter.AttemptCount);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WhenStalePrintingAndSpoolerSuccess_RecoversToSpoolAccepted()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var printer = new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            StoreId = 1,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Printers.Add(printer);

        var optionsModel = new PrintExecutionOptions
        {
            TimeoutSeconds = 1,
            MaxAttempts = 4,
            BackoffSeconds = [1, 2, 3, 4],
            PrintSettings = "fit"
        };
        var staleAfter = TimeSpan.FromSeconds(optionsModel.TimeoutSeconds + 10);

        var now = DateTimeOffset.UtcNow;
        var job = new PrintJob
        {
            JobId = Guid.NewGuid(),
            SourceSystem = "TEST",
            ExternalJobId = "EXT-5",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Printing,
            PrinterId = printer.PrinterId,
            AttemptCount = 0,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now - staleAfter - TimeSpan.FromSeconds(1),
            RowVersion = Array.Empty<byte>()
        };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();

        var spooler = new FakeSpooler((_, __, ___) =>
            Task.FromResult(new PrintSpoolResult(true, null, null, false)));

        var options = Options.Create(optionsModel);
        var service = new PrintExecutionService(
            db,
            spooler,
            NullLogger<PrintExecutionService>.Instance,
            options,
            new DummyRoutingResolver(),
            TimeProvider.System);

        var processed = await service.ExecuteBatchAsync(batchSize: 1, CancellationToken.None);
        Assert.Equal(1, processed);
        Assert.Equal(1, spooler.CallCount);

        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == job.JobId);
        Assert.Equal(PrintJobStatus.SpoolAccepted, jobAfter.Status);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WhenSpoolerThrowsTransientExceptionAndAttemptsLeft_TransitionsToRetryScheduled()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var printer = new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            StoreId = 1,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Printers.Add(printer);

        var now = DateTimeOffset.UtcNow;
        var job = new PrintJob
        {
            JobId = Guid.NewGuid(),
            SourceSystem = "TEST",
            ExternalJobId = "EXT-6",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Routed,
            PrinterId = printer.PrinterId,
            AttemptCount = 0,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Array.Empty<byte>()
        };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();

        var spooler = new FakeSpooler((_, __, ___) => throw new InvalidOperationException("boom"));

        var optionsModel = new PrintExecutionOptions
        {
            TimeoutSeconds = 1,
            MaxAttempts = 4,
            BackoffSeconds = [1, 2, 3, 4],
            PrintSettings = "fit"
        };

        var options = Options.Create(optionsModel);
        var service = new PrintExecutionService(
            db,
            spooler,
            NullLogger<PrintExecutionService>.Instance,
            options,
            new DummyRoutingResolver(),
            TimeProvider.System);

        var processed = await service.ExecuteBatchAsync(batchSize: 1, CancellationToken.None);
        Assert.Equal(1, processed);

        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == job.JobId);
        Assert.Equal(PrintJobStatus.RetryScheduled, jobAfter.Status);
        Assert.Equal("SPOOLER_EXCEPTION", jobAfter.LastErrorCode);
        Assert.Equal("Error en spooler", jobAfter.LastErrorMessage);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WhenStalePrintingAndAttemptsExhausted_TransitionsToErrorFinalRetriesExhausted()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        var printer = new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = "\\\\srv\\q1",
            StoreId = 1,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Printers.Add(printer);

        var optionsModel = new PrintExecutionOptions
        {
            TimeoutSeconds = 1,
            MaxAttempts = 1,
            BackoffSeconds = [1, 2, 3, 4],
            PrintSettings = "fit"
        };
        var staleAfter = TimeSpan.FromSeconds(optionsModel.TimeoutSeconds + 10);

        var now = DateTimeOffset.UtcNow;
        var job = new PrintJob
        {
            JobId = Guid.NewGuid(),
            SourceSystem = "TEST",
            ExternalJobId = "EXT-7",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Printing,
            PrinterId = printer.PrinterId,
            AttemptCount = 1,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now - staleAfter - TimeSpan.FromSeconds(1),
            RowVersion = Array.Empty<byte>()
        };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();

        var spooler = new FakeSpooler((_, __, ___) =>
            Task.FromResult(new PrintSpoolResult(true, null, null, false)));

        var options = Options.Create(optionsModel);
        var service = new PrintExecutionService(
            db,
            spooler,
            NullLogger<PrintExecutionService>.Instance,
            options,
            new DummyRoutingResolver(),
            TimeProvider.System);

        var processed = await service.ExecuteBatchAsync(batchSize: 1, CancellationToken.None);
        Assert.Equal(1, processed);
        Assert.Equal(0, spooler.CallCount);

        var jobAfter = await db.PrintJobs.FirstAsync(j => j.JobId == job.JobId);
        Assert.Equal(PrintJobStatus.ErrorFinal, jobAfter.Status);
        Assert.Equal("RETRIES_EXHAUSTED", jobAfter.LastErrorCode);
        Assert.Contains("Intentos agotados", jobAfter.LastErrorMessage ?? string.Empty);
    }
}

internal sealed class DummyRoutingResolver : IRoutingResolver
{
    public Task<int?> ResolvePrinterAsync(
        int storeId,
        string documentType,
        string channel,
        CancellationToken cancellationToken = default)
    {
        // En estos tests el job ya tiene PrinterId y el resolutor no se usa.
        return Task.FromResult<int?>(null);
    }
}

