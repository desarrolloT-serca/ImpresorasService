using ImpresorasService.Application.Abstractions;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// Implementación que no envía a impresora real. Útil para desarrollo/CI sin impresoras.
/// Simula éxito por defecto; configurable para simular fallos.
/// </summary>
public sealed class NoOpPrintSpooler : IPrinterSpooler
{
    private readonly bool _simulateSuccess;

    public NoOpPrintSpooler(bool simulateSuccess = true)
    {
        _simulateSuccess = simulateSuccess;
    }

    public Task<PrintSpoolResult> SendToPrinterAsync(
        byte[] pdfBlob,
        string spoolQueueName,
        CancellationToken cancellationToken = default)
    {
        if (_simulateSuccess)
            return Task.FromResult(new PrintSpoolResult(true, null, null, false));

        return Task.FromResult(new PrintSpoolResult(false, "SPOOLER_DOWN", "NoOp: simulación de fallo", true));
    }
}
