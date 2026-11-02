using ImpresorasService.Application.Abstractions;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// Servidor que imprime solo por IP (<c>PrintExecution:SoloIp</c>, p. ej. Linux): no hay cola de
/// Windows a la que recurrir. <c>PrintExecutionService</c> no la llama: con ella, si por IP no se
/// puede, queda el motivo de IPP. Si algo la llamara, falla sin simular nada.
/// </summary>
public sealed class SinColaPrintSpooler : IPrinterSpooler
{
    public Task<PrintSpoolResult> SendToPrinterAsync(byte[] pdfBlob, string spoolQueueName, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PrintSpoolResult(false, "NO_PRINT_ROUTE",
            "Este servidor imprime solo por IP: no usa colas de Windows.", false));
}
