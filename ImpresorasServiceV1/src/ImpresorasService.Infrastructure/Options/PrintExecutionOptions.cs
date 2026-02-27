namespace ImpresorasService.Infrastructure.Options;

public sealed class PrintExecutionOptions
{
    public const string SectionName = "PrintExecution";

    /// <summary>Intervalo de polling en segundos para jobs Routed.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Tamaño del lote por ciclo.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>Timeout por intento de impresión en segundos.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Máximo número de intentos por job.</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>Backoff en segundos: 15, 30, 60, 90.</summary>
    public int[] BackoffSeconds { get; set; } = [15, 30, 60, 90];

    /// <summary>Ruta al ejecutable para imprimir PDF (ej. SumatraPDF). Si vacío, se intenta ubicación por defecto.</summary>
    public string? PdfPrinterExecutablePath { get; set; }
}
