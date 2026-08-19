using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

public sealed class PrintExecutionServiceTests
{
    [Fact]
    public async Task ExecuteBatchAsync_processes_due_retry_jobs_even_when_future_retries_exist()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;
        var now = DateTimeOffset.UtcNow;
        var printerId = 10;
        var dueJobId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

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
            PrinterId = printerId,
            PrinterName = "Printer 1",
            SpoolQueue = "QUEUE",
            StoreId = 1,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        for (var i = 1; i <= 80; i++)
        {
            db.PrintJobs.Add(MakeRetryJob(
                new Guid($"00000000-0000-0000-0000-{i:000000000000}"),
                printerId,
                now.AddMinutes(i)));
        }

        db.PrintJobs.Add(MakeRetryJob(dueJobId, printerId, now.AddSeconds(-5)));
        await db.SaveChangesAsync();

        var spooler = new FakeSpooler((_, _, _) => Task.FromResult(
            new PrintSpoolResult(false, "OFFLINE", "Impresora desconectada", true)));
        var service = new PrintExecutionService(
            db,
            spooler,
            NullLogger<PrintExecutionService>.Instance,
            Options.Create(new PrintExecutionOptions
            {
                BatchSize = 10,
                MaxAttempts = 4,
                BackoffSeconds = [15, 30, 60, 90],
                TimeoutSeconds = 30
            }),
            new NullRoutingResolver(),
            TimeProvider.System);

        var processed = await service.ExecuteBatchAsync(10);
        // El servicio escribe con ExecuteUpdate, que no refresca las entidades ya seguidas por
        // este contexto. En produccion cada ciclo abre su propio scope; aqui hay que soltarlas
        // a mano o las aserciones leerian la copia en memoria de antes del UPDATE.
        db.ChangeTracker.Clear();

        var dueJob = await db.PrintJobs.SingleAsync(j => j.JobId == dueJobId);
        Assert.Equal(1, processed);
        Assert.Equal(1, spooler.CallCount);
        Assert.Equal(2, dueJob.AttemptCount);
        Assert.Equal(PrintJobStatus.RetryScheduled, dueJob.Status);
        Assert.True(dueJob.NextRetryAtUtc > now);
    }

    private static PrintJob MakeRetryJob(Guid jobId, int printerId, DateTimeOffset nextRetryAtUtc)
    {
        return new PrintJob
        {
            JobId = jobId,
            SourceSystem = "TEST",
            ExternalJobId = "JOB-" + jobId.ToString("N"),
            StoreId = 1,
            DocumentType = "ALBARAN",
            Channel = "DEFAULT",
            PdfBlob = [0x25, 0x50, 0x44, 0x46],
            PdfSha256 = "abc123",
            Status = PrintJobStatus.RetryScheduled,
            PrinterId = printerId,
            AttemptCount = 1,
            NextRetryAtUtc = nextRetryAtUtc,
            CorrelationId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private sealed class NullRoutingResolver : IRoutingResolver
    {
        public Task<int?> ResolvePrinterAsync(
            int storeId,
            string documentType,
            string channel,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(null);
        }

        public Task<IReadOnlyList<int?>> ResolveBatchAsync(
            IReadOnlyList<(int storeId, string documentType, string channel)> requests,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<int?>>(requests.Select(_ => (int?)null).ToArray());
        }
    }
}
