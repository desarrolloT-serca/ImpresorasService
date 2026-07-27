namespace ImpresorasService.Infrastructure.Options;

public sealed class DashboardThresholdRulesOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>
    /// Ruta del fichero JSON con las reglas de severidad de 1-3 niveles (G5.3). Compartido por
    /// Api y Worker — deben apuntar al mismo fichero (mismo valor en ambos appsettings.json,
    /// normalmente la carpeta de instalación compartida, sibling de Api/ y Worker/).
    /// </summary>
    public string? ThresholdRulesFilePath { get; set; }
}
