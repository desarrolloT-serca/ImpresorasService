using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Models;
using ImpresorasService.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Adapters;

public class ConfigurableJobSourceAdapter : IJobSourceAdapter
{
    private readonly SourceOptions _options;
    private readonly SqlTestJobSourceAdapter _sqlAdapter;
    private readonly SapHanaJobSourceAdapter _sapHanaAdapter;

    public ConfigurableJobSourceAdapter(
        IOptions<SourceOptions> options,
        SqlTestJobSourceAdapter sqlAdapter,
        SapHanaJobSourceAdapter sapHanaAdapter)
    {
        _options = options.Value;
        _sqlAdapter = sqlAdapter;
        _sapHanaAdapter = sapHanaAdapter;
    }

    public Task<IReadOnlyList<IncomingPrintJob>> FetchPendingJobsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        return _options.Mode.ToLowerInvariant() switch
        {
            "sqltest" => _sqlAdapter.FetchPendingJobsAsync(batchSize, cancellationToken),
            "saphana" => _sapHanaAdapter.FetchPendingJobsAsync(batchSize, cancellationToken),
            _ => Task.FromResult<IReadOnlyList<IncomingPrintJob>>(Array.Empty<IncomingPrintJob>())
        };
    }
}
