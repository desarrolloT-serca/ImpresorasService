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

    /// <summary>
    /// Lo mismo sobre <c>printer_source_print_job</c>, donde esta el grueso del volumen: la medicion
    /// del 12/08/2026 encontro el 100 % de las filas conservando su PDF, todas ya procesadas. El
    /// corte es <c>IsProcessed</c> + antiguedad de <c>CreatedAtUtc</c> (la tabla no tiene columna de
    /// actualizacion, y una fila se procesa a los segundos de crearse).
    /// Una fila sin procesar NUNCA se toca: su PDF es el unico ejemplar que existe todavia.
    /// </summary>
    public static async Task<int> ReleaseExpiredSourcePdfsAsync(
        ImpresorasDbContext db,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken ct = default)
    {
        var processed = true;
        var ids = await db.SourcePrintJobs
            .AsNoTracking()
            .Where(r => r.IsProcessed == processed
                && r.CreatedAtUtc <= cutoff
                && r.PdfBlob != null)
            .OrderBy(r => r.CreatedAtUtc)
            .Select(r => r.Id)
            .Take(Math.Max(1, batchSize))
            .ToListAsync(ct);

        if (ids.Count == 0)
            return 0;

        return await db.SourcePrintJobs
            .Where(r => ids.Contains(r.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PdfBlob, (byte[]?)null), ct);
    }
}
