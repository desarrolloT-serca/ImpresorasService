using ImpresorasService.Domain;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// Liberación del PDF de los trabajos ya cerrados. El hosting (intervalo, lock, logging) vive en
/// PdfRetentionBackgroundService; aquí solo la regla de qué se libera y cuándo.
/// </summary>
public static class PdfRetention
{
    /// <summary>
    /// Estados desde los que el trabajo ya no vuelve al flujo por sí solo. PrinterBlocked queda
    /// fuera a propósito: el watchdog todavía lo mueve, así que aún no está cerrado.
    /// </summary>
    public static readonly PrintJobStatus[] TerminalStatuses =
    [
        PrintJobStatus.PrintedConfirmed,
        PrintJobStatus.PrintedUnknown,
        PrintJobStatus.Cancelled,
        PrintJobStatus.ErrorFinal
    ];

    /// <summary>
    /// Pone a NULL el PDF de hasta <paramref name="batchSize"/> trabajos en estado terminal cuya
    /// última actualización sea anterior a <paramref name="cutoff"/>. Devuelve cuántos liberó.
    /// Conserva fila, <c>pdf_sha256</c> y metadatos: la trazabilidad y los KPI no dependen del blob.
    /// </summary>
    public static async Task<int> ReleaseExpiredPdfsAsync(
        ImpresorasDbContext db,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken ct = default)
    {
        // Ids primero: ExecuteUpdate no admite Take, y sin límite un backlog histórico se
        // convertiría en un UPDATE de millones de filas en una sola transacción.
        // La proyección no incluye PdfBlob, así que no se cargan blobs en memoria en ningún momento.
        var ids = await db.PrintJobs
            .AsNoTracking()
            .Where(j => TerminalStatuses.Contains(j.Status)
                && j.UpdatedAtUtc <= cutoff
                && j.PdfBlob != null)
            .OrderBy(j => j.UpdatedAtUtc)
            .Select(j => j.JobId)
            .Take(Math.Max(1, batchSize))
            .ToListAsync(ct);

        if (ids.Count == 0)
            return 0;

        return await db.PrintJobs
            .Where(j => ids.Contains(j.JobId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PdfBlob, (byte[]?)null), ct);
    }
}
