namespace ImpresorasService.Infrastructure.Options;

/// <summary>Origen de los trabajos: la tabla printer_source_print_job que escribe el ERP.</summary>
public sealed class SourceOptions
{
    public const string SectionName = "Source";

    /// <summary>Etiqueta con la que se guardan los trabajos ingeridos (parte de la clave de idempotencia).</summary>
    public string SourceSystem { get; set; } = "ERP";

    /// <summary>Segundos que una fila reclamada queda reservada a este Worker (mínimo 15).</summary>
    public int LeaseSeconds { get; set; } = 90;

    /// <summary>Hosts de los que se acepta descargar pdf_url (solo https). Vacío: ninguno.</summary>
    public string[] PdfUrlHosts { get; set; } = [];
}
