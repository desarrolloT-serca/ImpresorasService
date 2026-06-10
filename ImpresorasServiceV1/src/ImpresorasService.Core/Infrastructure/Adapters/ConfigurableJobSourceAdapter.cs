using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Models;
using ImpresorasService.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Adapters;

public class ConfigurableJobSourceAdapter : IJobSourceAdapter
{
    private readonly SourceOptions _options;
    private readonly SapHanaJobSourceAdapter _sapHanaAdapter;

    public ConfigurableJobSourceAdapter(
        IOptions<SourceOptions> options,
        SapHanaJobSourceAdapter sapHanaAdapter)
    {
        _options = options.Value;
        _sapHanaAdapter = sapHanaAdapter;
    }

    public Task<IReadOnlyList<IncomingPrintJob>> FetchPendingJobsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        EnsureSapHanaMode();
        return _sapHanaAdapter.FetchPendingJobsAsync(batchSize, cancellationToken);
    }

    public Task MarkJobsProcessedAsync(
        IReadOnlyList<long> sourceJobIds,
        CancellationToken cancellationToken)
    {
        EnsureSapHanaMode();
        return _sapHanaAdapter.MarkJobsProcessedAsync(sourceJobIds, cancellationToken);
    }

    public Task RenewJobLeasesAsync(IReadOnlyList<long> sourceJobIds, CancellationToken cancellationToken)
    {
        EnsureSapHanaMode();
        return _sapHanaAdapter.RenewJobLeasesAsync(sourceJobIds, cancellationToken);
    }

    private void EnsureSapHanaMode()
    {
        if (!string.Equals(_options.Mode, "SapHana", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Source:Mode '{_options.Mode}' no es compatible. El único modo operativo soportado es 'SapHana'.");
        }
    }
}
