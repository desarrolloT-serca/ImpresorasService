namespace ImpresorasService.Application.Abstractions;

public enum IppOutcome
{
    PrinterIdle,       // estado 3: impresora libre → trabajo confirmado
    PrinterProcessing, // estado 4: imprimiendo → esperar a que termine
    PrinterStopped,    // estado 5: detenida (sin papel, atasco…) → esperar recuperación
    Unavailable,       // sin respuesta IPP o no soportado
    JobAborted,        // el trabajo enviado por IP acabó cancelado o abortado en la impresora
    CompletedWithErrors // la impresora lo dio por terminado, pero con errores
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
    /// Si no ha salido nada (no responde, no acepta el formato, rechaza el trabajo) devuelve el
    /// motivo con <c>SinEnviar</c>, y se puede probar por la cola; <c>null</c> si el host no vale.
    /// Si el envío se corta a medias devuelve <c>NET_TIMEOUT</c>, y si la impresora falla con el
    /// trabajo a medias, <c>IPP_ERROR_WHILE_PRINTING</c>: puede haberse impreso y no se reintenta.
    /// <paramref name="comoImagen"/>: la impresora ya abortó este documento en PDF; se manda como imagen.
    /// </summary>
    Task<PrintSpoolResult?> PrintPdfAsync(string printerHost, byte[] pdf, string jobName, bool comoImagen, CancellationToken ct);

    /// <summary>
    /// Estado de un trabajo enviado por IP (Get-Job-Attributes): terminado = <see cref="IppOutcome.PrinterIdle"/>,
    /// en cola o imprimiendo = <see cref="IppOutcome.PrinterProcessing"/>, detenido =
    /// <see cref="IppOutcome.PrinterStopped"/>, cancelado o abortado = <see cref="IppOutcome.JobAborted"/>.
    /// <c>null</c> si la impresora no responde o ya no lo recuerda: entonces vale el estado de la impresora.
    /// </summary>
    Task<IppQueryResult?> QueryJobStateAsync(string printerHost, int jobId, CancellationToken ct);

    /// <summary>
    /// Cancela en la impresora un trabajo enviado por IP, al cancelarlo una persona en la aplicación
    /// (decisión del usuario, 27/10/2026: si no, sale al recuperarse la impresora y duplica en silencio).
    /// Antes comprueba por su nombre que ese número sigue siendo el nuestro: una impresora que se
    /// reinicia puede reutilizar números y se cancelaría otro documento (3b.8).
    /// </summary>
    Task<CancelacionEnImpresora> CancelJobAsync(string printerHost, int jobId, string jobName, CancellationToken ct);

    /// <summary>
    /// El job-name con que se manda un trabajo a la impresora (como mucho 100 caracteres). El mismo al
    /// mandarlo y al comprobar que un número de trabajo sigue siendo el nuestro antes de cancelarlo.
    /// </summary>
    static string JobNameFor(string documentType, string externalJobId)
    {
        var nombre = $"{documentType} {externalJobId}";
        return nombre.Length > 100 ? nombre[..100] : nombre;
    }
}

/// <summary>Qué pasó al cancelar en la impresora un trabajo que se cancela en la aplicación.</summary>
public enum CancelacionEnImpresora
{
    /// <summary>Estaba en su cola y lo ha cancelado: no saldrá.</summary>
    Cancelado,
    /// <summary>Ya lo había impreso (job-state 9).</summary>
    YaSalio,
    /// <summary>Ya estaba cancelado o abortado (job-state 7-8): no saldrá.</summary>
    YaNoSaldra,
    /// <summary>No lo tiene en su cola (lo olvidó o se reinició): no saldrá; si salió, fue antes.</summary>
    NoLoTiene,
    /// <summary>Ese número es ahora otro documento: no se ha tocado.</summary>
    NoEsNuestro,
    /// <summary>No devuelve el nombre del trabajo: no se puede comprobar que sea el nuestro, no se toca.</summary>
    SinNombre,
    /// <summary>No responde.</summary>
    SinRespuesta,
    /// <summary>Lo tiene pero no deja cancelarlo por la red.</summary>
    NoDeja
}
