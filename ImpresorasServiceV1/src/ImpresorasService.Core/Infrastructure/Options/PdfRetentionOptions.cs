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
    /// Apagado por defecto a propósito: libera PDFs de forma irreversible. Activarlo es una decisión
    /// consciente, con un plazo de conservación ya acordado, y requiere que <c>pdf_blob</c> admita
    /// NULL en el esquema (scripts/sql/migrate_pdf_blob_nullable.sql).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Días que se conserva el PDF desde que el trabajo llega a un estado terminal. Debe ser mayor
    /// que la ventana en la que un operador todavía puede querer reimprimir un PrintedUnknown.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Horas entre barridos. No necesita ser frecuente: el crecimiento es diario.</summary>
    public int IntervalHours { get; set; } = 6;

    /// <summary>Trabajos por barrido, para no lanzar un UPDATE masivo contra HANA de una vez.</summary>
    public int BatchSize { get; set; } = 500;
}
