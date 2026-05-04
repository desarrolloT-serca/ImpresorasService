namespace ImpresorasService.Infrastructure.Options;

public sealed class SapHanaOptions
{
    public const string SectionName = "SapHana";

    public string ConnectionString { get; set; } = string.Empty;
    public string Schema { get; set; } = "ZTEST_VICENTE_2";
    public string Table { get; set; } = "printer_source_print_job";
    public string SourceSystem { get; set; } = "SAP-HANA";
    public int LeaseSeconds { get; set; } = 90;
}
