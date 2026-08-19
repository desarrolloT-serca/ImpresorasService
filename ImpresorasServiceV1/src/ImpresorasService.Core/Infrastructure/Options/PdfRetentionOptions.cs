namespace ImpresorasService.Infrastructure.Options;

/// <summary>
/// Retención del PDF en <c>printer_print_job</c>. El documento solo hace falta mientras el trabajo
/// puede volver a imprimirse; pasado ese plazo se libera el blob y se conservan la fila, el hash
/// (<c>pdf_sha256</c>) y todos los metadatos, de los que sí dependen la trazabilidad y los KPI.
/// </summary>
public sealed class PdfRetentionOptions
{
    public const string SectionName = "PdfRetention";

    /// <summary>
    /// Activado el 19/08/2026 con un plazo acordado de 90 días. Libera PDFs de forma irreversible y
    /// requiere que <c>pdf_blob</c> admita NULL en las dos tablas
    /// (scripts/sql/migrate_pdf_blob_nullable.sql); mientras ese DDL no esté aplicado, cada barrido
    /// deja un warning y no libera nada.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Días que se conserva el PDF desde que el trabajo llega a un estado terminal, y desde que se
    /// procesa una fila del origen. Debe ser mayor que la ventana en la que un operador todavía puede
    /// querer reimprimir un PrintedUnknown. 90 por decisión del 19/08/2026.
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>Horas entre barridos. No necesita ser frecuente: el crecimiento es diario.</summary>
    public int IntervalHours { get; set; } = 6;

    /// <summary>Trabajos por barrido, para no lanzar un UPDATE masivo contra HANA de una vez.</summary>
    public int BatchSize { get; set; } = 500;
}
