namespace ImpresorasService.Infrastructure.Options;

public sealed class SourceOptions
{
    public const string SectionName = "Source";
    public string Mode { get; set; } = "SqlTest";
}
