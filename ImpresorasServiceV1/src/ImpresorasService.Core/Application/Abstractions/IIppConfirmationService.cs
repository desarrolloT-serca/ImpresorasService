namespace ImpresorasService.Application.Abstractions;

public enum IppOutcome
{
    PrinterIdle,       // estado 3: impresora libre → trabajo confirmado
    PrinterProcessing, // estado 4: imprimiendo → esperar a que termine
    PrinterStopped,    // estado 5: detenida (sin papel, atasco…) → esperar recuperación
    Unavailable,       // sin respuesta IPP o no soportado
    JobAborted         // el trabajo enviado por IP acabó cancelado o abortado en la impresora
}

public record IppQueryResult(IppOutcome Outcome, string? ErrorCode = null, string? ErrorMessage = null);

public interface IIppConfirmationService
{
    /// <summary>
    /// Consulta el estado IPP del printer en <paramref name="printerHost"/>.
    /// Devuelve <see cref="IppOutcome.PrinterIdle"/> si el printer está disponible y sin error,
    /// <see cref="IppOutcome.PrinterStopped"/> si el printer reporta un estado detenido/error,
    /// o <see cref="IppOutcome.Unavailable"/> si IPP no responde o no está disponible.
    /// </summary>
    Task<IppQueryResult> QueryPrinterStateAsync(string printerHost, CancellationToken ct);

    /// <summary>
    /// Imprime el PDF directamente en la impresora por IPP (Print-Job), sin cola de Windows.
    /// Devuelve <c>null</c> si no se ha enviado nada y se puede probar por la cola: sin respuesta
    /// IPP, sin PDF entre los formatos que acepta, o trabajo rechazado por la impresora. Si el envío
    /// se corta a medias devuelve <c>NET_TIMEOUT</c>: puede haberse impreso y no se reintenta.
    /// </summary>
    Task<PrintSpoolResult?> PrintPdfAsync(string printerHost, byte[] pdf, string jobName, CancellationToken ct);

    /// <summary>
    /// Estado de un trabajo enviado por IP (Get-Job-Attributes): terminado = <see cref="IppOutcome.PrinterIdle"/>,
    /// en cola o imprimiendo = <see cref="IppOutcome.PrinterProcessing"/>, detenido =
    /// <see cref="IppOutcome.PrinterStopped"/>, cancelado o abortado = <see cref="IppOutcome.JobAborted"/>.
    /// <c>null</c> si la impresora no responde o ya no lo recuerda: entonces vale el estado de la impresora.
    /// </summary>
    Task<IppQueryResult?> QueryJobStateAsync(string printerHost, int jobId, CancellationToken ct);
}
