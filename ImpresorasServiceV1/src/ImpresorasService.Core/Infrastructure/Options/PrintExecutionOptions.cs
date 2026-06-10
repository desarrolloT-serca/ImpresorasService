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

    /// <summary>
    /// Ajustes de impresión para SumatraPDF (por defecto "fit" para mejor compatibilidad).
    /// Ejemplos: fit, noscale, portrait, landscape.
    /// </summary>
    public string PrintSettings { get; set; } = "fit";

    /// <summary>Si true, no borra el PDF temporal cuando falla (para depurar en %TEMP%).</summary>
    public bool KeepTempFileOnFailure { get; set; }

    /// <summary>
    /// Ventana máxima permitida para un job en <c>SpoolAccepted</c>.
    /// Si supera este tiempo sin avanzar, se marca como <c>PrintedUnknown</c>.
    /// </summary>
    public int SpoolAcceptedMaxAgeSeconds { get; set; } = 120;

    /// <summary>Intervalo del watchdog (segundos) para jobs en <c>SpoolAccepted</c>.</summary>
    public int SpoolAcceptedWatchIntervalSeconds { get; set; } = 10;

    /// <summary>Tamaño de lote máximo por ciclo del watchdog.</summary>
    public int SpoolAcceptedWatchBatchSize { get; set; } = 50;
}
