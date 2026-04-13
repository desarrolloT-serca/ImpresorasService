namespace ImpresorasService.Infrastructure.Options;

public sealed class SourceOptions
{
    public const string SectionName = "Source";
    public string Mode { get; set; } = "SqlTest";

    /// <summary>
    /// Segundos de arrendamiento al reclamar filas en <see cref="Mode"/> SqlTest (varios workers sobre la misma BD).
    /// </summary>
    public int SqlTestLeaseSeconds { get; set; } = 120;
}
