namespace ImpresorasService.Infrastructure.Options;

public sealed class SapPostgresOptions
{
    public const string SectionName = "SapPostgres";

    /// <summary>
    /// Connection string para el PostgreSQL remoto de SAP.
    /// Se recomienda usar el mismo formato que Npgsql (host/port/db/user/password).
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Esquema donde vive la tabla auxiliar (ej. 'sap').
    /// </summary>
    public string Schema { get; set; } = "sap";

    /// <summary>
    /// Nombre de la tabla auxiliar (ej. 'print_queue_aux').
    /// </summary>
    public string Table { get; set; } = "print_queue_aux";

    /// <summary>
    /// Worker lease en segundos (solo evita dobles claims mientras el worker procesa).
    /// Debe cubrir el p95 de ingesta local hasta el ack; si no, otro worker puede reclamar el mismo id.
    /// </summary>
    public int LeaseSeconds { get; set; } = 90;
}

