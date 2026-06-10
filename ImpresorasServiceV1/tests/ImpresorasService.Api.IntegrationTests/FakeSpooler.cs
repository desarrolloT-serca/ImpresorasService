using System;
using System.Threading;
using System.Threading.Tasks;
using ImpresorasService.Application.Abstractions;

namespace ImpresorasService.Api.IntegrationTests;

internal sealed class FakeSpooler : IPrinterSpooler
{
    private readonly Func<byte[], string, CancellationToken, Task<PrintSpoolResult>> _behavior;

    public FakeSpooler(Func<byte[], string, CancellationToken, Task<PrintSpoolResult>> behavior)
    {
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
    }

    public int CallCount { get; private set; }
    public string? LastQueueName { get; private set; }
    public byte[]? LastPdfBlob { get; private set; }

    public async Task<PrintSpoolResult> SendToPrinterAsync(
        byte[] pdfBlob,
        string spoolQueueName,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastQueueName = spoolQueueName;
        LastPdfBlob = pdfBlob;
        return await _behavior(pdfBlob, spoolQueueName, cancellationToken);
    }
}

