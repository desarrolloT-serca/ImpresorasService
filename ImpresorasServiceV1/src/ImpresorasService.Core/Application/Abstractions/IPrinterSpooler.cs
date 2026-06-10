namespace ImpresorasService.Application.Abstractions;

/// <summary>
/// Resultado de un intento de impresión contra el spooler.
/// </summary>
public sealed record PrintSpoolResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsTransient);

/// <summary>
/// Abstracción para enviar trabajos al spooler de impresión de Windows.
/// </summary>
public interface IPrinterSpooler
{
    /// <summary>
    /// Envía el PDF a la cola de impresión indicada.
    /// </summary>
    /// <param name="pdfBlob">Contenido del PDF.</param>
    /// <param name="spoolQueueName">Nombre de la cola (ej. \\servidor\impresora o nombre local).</param>
    /// <param name="cancellationToken">Cancelación (ej. timeout 30s).</param>
    Task<PrintSpoolResult> SendToPrinterAsync(
        byte[] pdfBlob,
        string spoolQueueName,
        CancellationToken cancellationToken = default);
}
