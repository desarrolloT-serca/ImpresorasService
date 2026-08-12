using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// Retención del PDF: se libera el de los trabajos ya cerrados y caducados, y solo ese.
/// Lo que este test protege de verdad es lo que NO debe borrarse: un trabajo aún vivo, o uno
/// cerrado hace poco que un operador todavía puede querer reimprimir.
/// </summary>
public sealed class PdfRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReleaseExpiredPdfsAsync_ReleasesOnlyClosedJobsPastRetention()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        // Cerrado hace 40 días: se libera.
        var caducado = NewJob("EXT-CADUCADO", PrintJobStatus.PrintedConfirmed, Now.AddDays(-40));
        // Cerrado hace 5 días: dentro del plazo, se conserva.
        var reciente = NewJob("EXT-RECIENTE", PrintJobStatus.PrintedConfirmed, Now.AddDays(-5));
        // Sin confirmar hace 40 días: cerrado y caducado, se libera.
        var inciertoViejo = NewJob("EXT-INCIERTO-VIEJO", PrintJobStatus.PrintedUnknown, Now.AddDays(-40));
        // Sin confirmar hace 2 días: el operador aún puede reimprimirlo, NO se toca.
        var inciertoReciente = NewJob("EXT-INCIERTO-NUEVO", PrintJobStatus.PrintedUnknown, Now.AddDays(-2));
        // Vivo pero antiguo: no está cerrado, NO se toca aunque supere el plazo.
        var enCola = NewJob("EXT-EN-COLA", PrintJobStatus.Routed, Now.AddDays(-90));
        // Bloqueado: el watchdog todavía lo mueve, NO se toca.
        var bloqueado = NewJob("EXT-BLOQUEADO", PrintJobStatus.PrinterBlocked, Now.AddDays(-90));

        db.PrintJobs.AddRange(caducado, reciente, inciertoViejo, inciertoReciente, enCola, bloqueado);
        await db.SaveChangesAsync();

        var released = await PdfRetention.ReleaseExpiredPdfsAsync(db, Now.AddDays(-30), batchSize: 500);

        Assert.Equal(2, released);

        db.ChangeTracker.Clear();
        Assert.Null(await PdfOf(db, "EXT-CADUCADO"));
        Assert.Null(await PdfOf(db, "EXT-INCIERTO-VIEJO"));

        Assert.NotNull(await PdfOf(db, "EXT-RECIENTE"));
        Assert.NotNull(await PdfOf(db, "EXT-INCIERTO-NUEVO"));
        Assert.NotNull(await PdfOf(db, "EXT-EN-COLA"));
        Assert.NotNull(await PdfOf(db, "EXT-BLOQUEADO"));

        // La fila y su trazabilidad siguen ahí: solo desaparece el documento.
        var liberado = await db.PrintJobs.AsNoTracking().SingleAsync(j => j.ExternalJobId == "EXT-CADUCADO");
        Assert.Equal("sha-de-prueba", liberado.PdfSha256);
        Assert.Equal(PrintJobStatus.PrintedConfirmed, liberado.Status);
    }

    [Fact]
    public async Task ReleaseExpiredPdfsAsync_RespectsBatchSize()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;

        for (var i = 0; i < 5; i++)
            db.PrintJobs.Add(NewJob($"EXT-{i}", PrintJobStatus.Cancelled, Now.AddDays(-40)));
        await db.SaveChangesAsync();

        var released = await PdfRetention.ReleaseExpiredPdfsAsync(db, Now.AddDays(-30), batchSize: 2);

        Assert.Equal(2, released);
        db.ChangeTracker.Clear();
        Assert.Equal(3, await db.PrintJobs.CountAsync(j => j.PdfBlob != null));
    }

    private static async Task<byte[]?> PdfOf(Infrastructure.Persistence.ImpresorasDbContext db, string externalJobId)
        => await db.PrintJobs.AsNoTracking()
            .Where(j => j.ExternalJobId == externalJobId)
            .Select(j => j.PdfBlob)
            .SingleAsync();

    private static PrintJob NewJob(string externalJobId, PrintJobStatus status, DateTimeOffset updatedAtUtc) => new()
    {
        JobId = Guid.NewGuid(),
        SourceSystem = "TEST",
        ExternalJobId = externalJobId,
        StoreId = 1,
        DocumentType = "FACTURA",
        Channel = "DEFAULT",
        PdfBlob = MinimalPdf.Bytes,
        PdfSha256 = "sha-de-prueba",
        Status = status,
        AttemptCount = 0,
        CorrelationId = Guid.NewGuid(),
        CreatedAtUtc = updatedAtUtc,
        UpdatedAtUtc = updatedAtUtc,
        RowVersion = Array.Empty<byte>()
    };
}
