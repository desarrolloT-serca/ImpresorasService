namespace ImpresorasService.Infrastructure.Options;

public sealed class SapHanaOptions
{
    public const string SectionName = "SapHana";

    public string ConnectionString { get; set; } = string.Empty;
    public string Schema { get; set; } = "SAP";
    public string Table { get; set; } = "PRINT_QUEUE_AUX";
    public string SourceSystem { get; set; } = "SAP-HANA";
    public int LeaseSeconds { get; set; } = 90;
}
