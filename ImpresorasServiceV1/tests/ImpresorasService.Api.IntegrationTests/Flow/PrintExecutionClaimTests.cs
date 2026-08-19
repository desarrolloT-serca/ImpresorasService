using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Flow;

/// <summary>
/// Claim atómico (Fase 2.2). El riesgo que cubre es el peor del sistema: dos Workers mandando el
/// mismo documento al spooler y saliendo el papel dos veces. La exclusión la da el estado en el
/// WHERE del UPDATE, no una comprobación previa en memoria.
/// </summary>
public sealed class PrintExecutionClaimTests
{
    [Fact]
    public async Task TwoWorkersOnTheSameJob_OnlyOneSendsToTheSpooler()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var jobId = await SeedRoutedJobAsync(setup.Db);

        // Dos contextos sobre la misma base: dos procesos, cada uno con su seguimiento.
        using var dbA = SqliteTestDbHelper.NewContext(setup.Connection);
        using var dbB = SqliteTestDbHelper.NewContext(setup.Connection);

        var spoolerA = new FakeSpooler((_, __, ___) => Task.FromResult(new PrintSpoolResult(true, null, null, false)));
        var spoolerB = new FakeSpooler((_, __, ___) => Task.FromResult(new PrintSpoolResult(true, null, null, false)));

        var processedA = await NewService(dbA, spoolerA).ExecuteBatchAsync(10, CancellationToken.None);
        // B barre después de que A haya reclamado: su UPDATE condicional no encuentra el estado.
        var processedB = await NewService(dbB, spoolerB).ExecuteBatchAsync(10, CancellationToken.None);

        Assert.Equal(1, spoolerA.CallCount);
        Assert.Equal(0, spoolerB.CallCount);
        Assert.Equal(1, processedA);
        Assert.Equal(0, processedB);

        setup.Db.ChangeTracker.Clear();
        var job = await setup.Db.PrintJobs.AsNoTracking().FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.SpoolAccepted, job.Status);
        // Un solo intento consumido: el segundo Worker no llegó a incrementarlo.
        Assert.Equal(1, job.AttemptCount);
    }

    /// <summary>
    /// Si un operador cancela mientras el spooler trabaja, su decisión manda: la resolución del envío
    /// exige seguir en Printing y no puede resucitar un trabajo ya cerrado.
    /// </summary>
    [Fact]
    public async Task CancelledWhileSpooling_IsNotOverwrittenByTheResult()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var jobId = await SeedRoutedJobAsync(setup.Db);

        using var dbWorker = SqliteTestDbHelper.NewContext(setup.Connection);

        // El "operador" cancela justo mientras el envío está en curso.
        var spooler = new FakeSpooler(async (_, __, ___) =>
        {
            using var dbOperator = SqliteTestDbHelper.NewContext(setup.Connection);
            await dbOperator.PrintJobs
                .Where(j => j.JobId == jobId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PrintJobStatus.Cancelled));

            return new PrintSpoolResult(true, null, null, false);
        });

        await NewService(dbWorker, spooler).ExecuteBatchAsync(10, CancellationToken.None);

        setup.Db.ChangeTracker.Clear();
        var job = await setup.Db.PrintJobs.AsNoTracking().FirstAsync(j => j.JobId == jobId);
        Assert.Equal(PrintJobStatus.Cancelled, job.Status);
    }

    private static PrintExecutionService NewService(
        Infrastructure.Persistence.ImpresorasDbContext db, FakeSpooler spooler)
        => new(
            db,
            spooler,
            NullLogger<PrintExecutionService>.Instance,
            Options.Create(new PrintExecutionOptions
            {
                TimeoutSeconds = 30,
                MaxAttempts = 4,
                BackoffSeconds = [1, 2, 3],
                PrintSettings = "fit"
            }),
            new DummyRoutingResolver(),
            TimeProvider.System);

    private static async Task<Guid> SeedRoutedJobAsync(Infrastructure.Persistence.ImpresorasDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.Printers.Add(new Printer
        {
            PrinterId = 10,
            PrinterName = "P1",
            SpoolQueue = @"\srv\q1",
            StoreId = 1,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var jobId = Guid.NewGuid();
        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId,
            SourceSystem = "TEST",
            ExternalJobId = "EXT-CLAIM",
            StoreId = 1,
            DocumentType = "FACTURA",
            Channel = "DEFAULT",
            PdfBlob = MinimalPdf.Bytes,
            PdfSha256 = "pdf-sha",
            Status = PrintJobStatus.Routed,
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
