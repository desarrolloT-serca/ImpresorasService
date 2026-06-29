namespace ImpresorasService.Application.Abstractions;

public enum IppOutcome
{
    PrinterIdle,       // estado 3: impresora libre → trabajo confirmado
    PrinterProcessing, // estado 4: imprimiendo → esperar a que termine
    PrinterStopped,    // estado 5: detenida (sin papel, atasco…) → esperar recuperación
    Unavailable        // sin respuesta IPP o no soportado
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
}
