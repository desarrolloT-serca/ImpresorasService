using System.Data;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Models;
using ImpresorasService.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Diagnostics;

namespace ImpresorasService.Infrastructure.Adapters;

public sealed class SapPostgresJobSourceAdapter : IJobSourceAdapter
{
    private readonly SapPostgresOptions _options;
    private readonly ILogger<SapPostgresJobSourceAdapter> _logger;
    private readonly string _effectiveConnectionString;

    private readonly string _workerId;

    public SapPostgresJobSourceAdapter(
        IOptions<SapPostgresOptions> options,
        IConfiguration configuration,
        ILogger<SapPostgresJobSourceAdapter> logger)
    {
        _options = options.Value;
        _logger = logger;
        _effectiveConnectionString = ResolveConnectionString(_options.ConnectionString, configuration);

        // Identificador estable para el proceso durante su vida.
        // Se usa para 'claimed_by' y evitar ack de claims de otros workers.
        _workerId = $"{Environment.MachineName}-{Process.GetCurrentProcess().Id}";
    }

    public async Task<IReadOnlyList<IncomingPrintJob>> FetchPendingJobsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
            return Array.Empty<IncomingPrintJob>();

        if (string.IsNullOrWhiteSpace(_effectiveConnectionString))
        {
            _logger.LogWarning("SapPostgresJobSourceAdapter: ConnectionString vacia. No se hara ingesta. Configure SapPostgres:ConnectionString, ConnectionStrings:SapPostgres o la variable SAPPG_CONNECTION_STRING.");
            return Array.Empty<IncomingPrintJob>();
        }

        var fullTableName = $"{QuoteIdent(_options.Schema)}.{QuoteIdent(_options.Table)}";

        var leaseSeconds = Math.Max(1, _options.LeaseSeconds);

        var sql = $@"
WITH candidates AS (
    SELECT q.id
    FROM {fullTableName} q
    WHERE q.processed = FALSE
      AND (q.lease_expires_at_utc IS NULL OR q.lease_expires_at_utc <= NOW())
    ORDER BY q.created_at_utc, q.id
    LIMIT @batchSize
    FOR UPDATE SKIP LOCKED
),
claimed AS (
    UPDATE {fullTableName} q
    SET claimed_by = @workerId,
        lease_expires_at_utc = NOW() + (INTERVAL '1 second' * @leaseSeconds),
        updated_at_utc = NOW()
    FROM candidates c
    WHERE q.id = c.id
    RETURNING
        q.id,
        q.external_id,
        q.document_type,
        q.store_code,
        q.created_at_utc,
        q.pdf_blob
)
SELECT
    id,
    external_id,
    document_type,
    store_code,
    created_at_utc,
    pdf_blob
FROM claimed
ORDER BY created_at_utc, id;";

        var jobs = new List<IncomingPrintJob>(batchSize);

        await using var conn = new NpgsqlConnection(_effectiveConnectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("batchSize", batchSize);
        cmd.Parameters.AddWithValue("workerId", _workerId);
        cmd.Parameters.AddWithValue("leaseSeconds", leaseSeconds);

        // Importante: usa CommandBehavior.SequentialAccess si el blob puede ser grande.
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var externalJobId = reader.GetString(1);
            var documentType = reader.GetString(2);
            var storeCode = reader.GetString(3);
            var createdAtUtc = reader.GetFieldValue<DateTimeOffset>(4);
            var pdfBlob = ReadBytea(reader, 5);

            jobs.Add(new IncomingPrintJob(
                SourceJobId: id,
                SourceSystem: "SAP-POSTGRES",
                ExternalJobId: externalJobId,
                StoreId: NormalizeStoreCode(storeCode),
                DocumentType: documentType,
                Channel: "DEFAULT",
                PdfBlob: pdfBlob,
                CreatedAtUtc: createdAtUtc));
        }

        if (jobs.Count > 0)
            _logger.LogInformation("SapPostgresJobSourceAdapter reclamó {Count} jobs.", jobs.Count);

        return jobs;
    }

    public async Task MarkJobsProcessedAsync(
        IReadOnlyList<long> sourceJobIds,
        CancellationToken cancellationToken)
    {
        if (sourceJobIds is null || sourceJobIds.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(_effectiveConnectionString))
        {
            _logger.LogWarning("SapPostgresJobSourceAdapter: ConnectionString vacia. No se hara ack en origen.");
            return;
        }

        var fullTableName = $"{QuoteIdent(_options.Schema)}.{QuoteIdent(_options.Table)}";

        var sql = $@"
UPDATE {fullTableName}
SET processed = TRUE,
    claimed_by = NULL,
    lease_expires_at_utc = NULL,
    updated_at_utc = NOW()
WHERE id = ANY(@ids::bigint[])
  AND claimed_by = @workerId;";

        await using var conn = new NpgsqlConnection(_effectiveConnectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("workerId", _workerId);

        var ids = sourceJobIds.ToArray();
        cmd.Parameters.AddWithValue("ids", ids);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (rows < sourceJobIds.Count)
        {
            _logger.LogWarning(
                "SapPostgres ack: se pidieron {Expected} ids pero solo se actualizaron {Rows} filas (lease/claim distinto o ya procesados). WorkerId={WorkerId}",
                sourceJobIds.Count,
                rows,
                _workerId);
        }
        else if (rows > 0)
        {
            _logger.LogInformation("SapPostgresJobSourceAdapter ack procesó {Rows} jobs.", rows);
        }
    }

    public async Task RenewJobLeasesAsync(IReadOnlyList<long> sourceJobIds, CancellationToken cancellationToken)
    {
        if (sourceJobIds is null || sourceJobIds.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(_effectiveConnectionString))
            return;

        var fullTableName = $"{QuoteIdent(_options.Schema)}.{QuoteIdent(_options.Table)}";
        var leaseSeconds = Math.Max(1, _options.LeaseSeconds);

        var sql = $@"
UPDATE {fullTableName}
SET lease_expires_at_utc = NOW() + (INTERVAL '1 second' * @leaseSeconds),
    updated_at_utc = NOW()
WHERE id = ANY(@ids::bigint[])
  AND claimed_by = @workerId;";

        await using var conn = new NpgsqlConnection(_effectiveConnectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("leaseSeconds", leaseSeconds);
        cmd.Parameters.AddWithValue("workerId", _workerId);
        cmd.Parameters.AddWithValue("ids", sourceJobIds.ToArray());

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static int NormalizeStoreCode(string storeCode)
    {
        var s = (storeCode ?? string.Empty).Trim();
        if (s.Length == 0)
            return 0;

        s = s.TrimStart('0');
        if (s.Length == 0)
            s = "0";

        return int.TryParse(s, out var v) ? v : 0;
    }

    private static string QuoteIdent(string value)
    {
        // Para evitar inyección en schema/table. 'value' debe venir de config controlada.
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static byte[] ReadBytea(NpgsqlDataReader reader, int ordinal)
    {
        // Manejo simple: para bytea usamos GetFieldValue<byte[]>().
        // Si necesitases optimización para blobs grandes, se ajusta a streams.
        return reader.GetFieldValue<byte[]>(ordinal);
    }

    private static string ResolveConnectionString(string configuredConnectionString, IConfiguration configuration)
    {
        // Prioridad:
        // 1) SapPostgres:ConnectionString en appsettings/options
        // 2) ConnectionStrings:SapPostgres
        // 3) Variable de entorno SAPPG_CONNECTION_STRING
        var direct = (configuredConnectionString ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        var fromConnectionStrings = (configuration.GetConnectionString("SapPostgres") ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(fromConnectionStrings))
            return fromConnectionStrings;

        var fromEnv = (Environment.GetEnvironmentVariable("SAPPG_CONNECTION_STRING") ?? string.Empty).Trim();
        return fromEnv;
    }
}

