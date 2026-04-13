namespace ImpresorasService.Application.Options;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";
    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 100;
}
