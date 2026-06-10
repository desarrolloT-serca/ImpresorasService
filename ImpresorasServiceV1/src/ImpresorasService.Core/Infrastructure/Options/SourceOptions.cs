namespace ImpresorasService.Infrastructure.Options;

public sealed class SourceOptions
{
    public const string SectionName = "Source";

    /// <summary>Modo de ingesta. Producción: <c>SapHana</c>.</summary>
    public string Mode { get; set; } = "SapHana";
}
