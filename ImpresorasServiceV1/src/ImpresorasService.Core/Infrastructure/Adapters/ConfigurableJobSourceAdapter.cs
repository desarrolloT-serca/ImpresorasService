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
    private readonly SapPostgresJobSourceAdapter _sapPostgresAdapter;

    public ConfigurableJobSourceAdapter(
        IOptions<SourceOptions> options,
        SqlTestJobSourceAdapter sqlAdapter,
        SapHanaJobSourceAdapter sapHanaAdapter,
        SapPostgresJobSourceAdapter sapPostgresAdapter)
    {
        _options = options.Value;
        _sqlAdapter = sqlAdapter;
        _sapHanaAdapter = sapHanaAdapter;
        _sapPostgresAdapter = sapPostgresAdapter;
    }

    public Task<IReadOnlyList<IncomingPrintJob>> FetchPendingJobsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        return _options.Mode.ToLowerInvariant() switch
        {
            "sqltest" => _sqlAdapter.FetchPendingJobsAsync(batchSize, cancellationToken),
            "saphana" => _sapHanaAdapter.FetchPendingJobsAsync(batchSize, cancellationToken),
            "sappostgres" => _sapPostgresAdapter.FetchPendingJobsAsync(batchSize, cancellationToken),
            _ => Task.FromResult<IReadOnlyList<IncomingPrintJob>>(Array.Empty<IncomingPrintJob>())
        };
    }

    public Task MarkJobsProcessedAsync(
        IReadOnlyList<long> sourceJobIds,
        CancellationToken cancellationToken)
    {
        return _options.Mode.ToLowerInvariant() switch
        {
            "sqltest" => _sqlAdapter.MarkJobsProcessedAsync(sourceJobIds, cancellationToken),
            "saphana" => _sapHanaAdapter.MarkJobsProcessedAsync(sourceJobIds, cancellationToken),
            "sappostgres" => _sapPostgresAdapter.MarkJobsProcessedAsync(sourceJobIds, cancellationToken),
            _ => Task.CompletedTask
        };
    }

    public Task RenewJobLeasesAsync(IReadOnlyList<long> sourceJobIds, CancellationToken cancellationToken)
    {
        return _options.Mode.ToLowerInvariant() switch
        {
            "sqltest" => _sqlAdapter.RenewJobLeasesAsync(sourceJobIds, cancellationToken),
            "saphana" => _sapHanaAdapter.RenewJobLeasesAsync(sourceJobIds, cancellationToken),
            "sappostgres" => _sapPostgresAdapter.RenewJobLeasesAsync(sourceJobIds, cancellationToken),
            _ => Task.CompletedTask
        };
    }
}
