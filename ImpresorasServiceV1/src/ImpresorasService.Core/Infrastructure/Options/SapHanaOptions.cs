namespace ImpresorasService.Infrastructure.Options;

public sealed class SapHanaOptions
{
    public const string SectionName = "SapHana";

    // La conexión y el esquema salen solo de ConnectionStrings:PrintQueue ("Search Path").
    // SourceSystem etiqueta los trabajos ingeridos; desde el 23/10/2026 el origen es el ERP, no SAP.
    public string SourceSystem { get; set; } = "ERP";
    public int LeaseSeconds { get; set; } = 90;
}
