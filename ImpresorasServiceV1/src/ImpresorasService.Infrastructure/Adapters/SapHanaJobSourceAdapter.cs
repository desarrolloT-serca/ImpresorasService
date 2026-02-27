using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Models;
using ImpresorasService.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Adapters;

public class SapHanaJobSourceAdapter : IJobSourceAdapter
{
    private readonly SourceOptions _options;
    private readonly ILogger<SapHanaJobSourceAdapter> _logger;

    public SapHanaJobSourceAdapter(
        IOptions<SourceOptions> options,
        ILogger<SapHanaJobSourceAdapter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<IncomingPrintJob>> FetchPendingJobsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.Mode, "SapHana", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<IncomingPrintJob>>(Array.Empty<IncomingPrintJob>());
        }

        _logger.LogWarning(
            "Adaptador SAP HANA pendiente de implementacion concreta. Modo SapHana activo sin fetch operativo.");
        return Task.FromResult<IReadOnlyList<IncomingPrintJob>>(Array.Empty<IncomingPrintJob>());
    }
}
