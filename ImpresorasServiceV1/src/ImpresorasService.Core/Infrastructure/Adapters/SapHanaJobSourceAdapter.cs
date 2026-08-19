using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Models;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace ImpresorasService.Infrastructure.Adapters;

public class SapHanaJobSourceAdapter : IJobSourceAdapter
{
    private readonly SourceOptions _options;
    private readonly SapHanaOptions _hanaOptions;
    private readonly ImpresorasDbContext _dbContext;
    private readonly ILogger<SapHanaJobSourceAdapter> _logger;
    private readonly string _workerId;
    private readonly string _sourceSystem;

    // Token del último claim emitido por esta instancia (Scoped: una instancia = un ciclo de
    // polling = un FetchPendingJobsAsync seguido de su Mark/Renew). Exigirlo en ACK y renovación
    // (Fase 1.5) evita operar sobre una fila cuyo lease expiró y fue reclamada por otro worker.
    private string? _lastClaimToken;
    private readonly TimeProvider _timeProvider;

    public SapHanaJobSourceAdapter(
        IOptions<SourceOptions> options,
        IOptions<SapHanaOptions> hanaOptions,
        ImpresorasDbContext dbContext,
        ILogger<SapHanaJobSourceAdapter> logger,
        TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _options = options.Value;
        _hanaOptions = hanaOptions.Value;
        _dbContext = dbContext;
        _logger = logger;
        _workerId = $"{Environment.MachineName}-{Process.GetCurrentProcess().Id}";
        _sourceSystem = string.IsNullOrWhiteSpace(_hanaOptions.SourceSystem) ? "SAP-HANA" : _hanaOptions.SourceSystem.Trim();
    }

    public async Task<IReadOnlyList<IncomingPrintJob>> FetchPendingJobsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.Mode, "SapHana", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<IncomingPrintJob>();

        if (batchSize <= 0)
            return Array.Empty<IncomingPrintJob>();

        var claimToken = Guid.NewGuid().ToString("N");
        var now = _timeProvider.GetUtcNow();
        var leaseSeconds = Math.Max(15, _hanaOptions.LeaseSeconds);
        var leaseEnd = now.AddSeconds(leaseSeconds);

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var pendingOnly = false;

        // Selección de candidatos: proyección escalar sin PdfBlob (Fase 1.3) y filtro de
        // disponibilidad de claim + orden empujados a SQL antes del Take (Fase 1.2). Antes,
        // Take(batchSize*5) cortaba por Id antes de filtrar por claim: si las primeras filas
        // estaban reclamadas por otro worker, se perdía el ciclo aunque hubiera libres después,
        // y además se cargaba el blob completo de filas que ni siquiera se iban a reclamar.
        var candidateIds = await _dbContext.SourcePrintJobs
            .Where(x => x.IsProcessed == pendingOnly
                && (x.ClaimedUntilUtc == null || x.ClaimedUntilUtc <= now))
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0)
        {
            await tx.CommitAsync(cancellationToken);
            return Array.Empty<IncomingPrintJob>();
        }

        // Recarga con entidad completa (incluye PdfBlob) solo de las filas ya seleccionadas,
        // y re-verifica disponibilidad para reducir la ventana de carrera con otro worker.
        var claimedRows = (await _dbContext.SourcePrintJobs
            .Where(x => candidateIds.Contains(x.Id) && x.IsProcessed == pendingOnly)
            .ToListAsync(cancellationToken))
            .Where(x => x.ClaimedUntilUtc == null || x.ClaimedUntilUtc <= now)
            .ToList();

        if (claimedRows.Count == 0)
        {
            await tx.CommitAsync(cancellationToken);
            return Array.Empty<IncomingPrintJob>();
        }

        foreach (var row in claimedRows)
        {
            row.ClaimedBy = _workerId;
            row.ClaimedUntilUtc = leaseEnd;
            row.ClaimToken = claimToken;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        _lastClaimToken = claimToken;

        // Una fila sin PDF no puede imprimirse. No deberia existir entre las no procesadas -la
        // retencion solo toca las que ya lo estan-, pero si aparece se descarta con traza en vez de
        // reventar el lote entero al calcular el hash.
        var withoutPdf = claimedRows.Where(x => x.PdfBlob is null || x.PdfBlob.Length == 0).ToList();
        if (withoutPdf.Count > 0)
            _logger.LogError(
                "Origen: {Count} filas sin PDF pese a no estar procesadas; se omiten. Ids={Ids}",
                withoutPdf.Count, string.Join(",", withoutPdf.Select(x => x.Id)));

        var jobs = claimedRows
            .Where(x => x.PdfBlob is not null && x.PdfBlob.Length > 0)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Select(row => new IncomingPrintJob(
                SourceJobId: row.Id,
                SourceSystem: _sourceSystem,
                ExternalJobId: row.ExternalJobId,
                StoreId: row.StoreId,
                DocumentType: row.DocumentType,
                Channel: string.IsNullOrWhiteSpace(row.Channel) ? "DEFAULT" : row.Channel,
                PdfBlob: row.PdfBlob!,
                CreatedAtUtc: row.CreatedAtUtc))
            .ToList();

        _logger.LogInformation("SapHana adapter reclamó {Count} trabajos pendientes.", jobs.Count);
        return jobs;
    }

    public async Task MarkJobsProcessedAsync(
        IReadOnlyList<long> sourceJobIds,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.Mode, "SapHana", StringComparison.OrdinalIgnoreCase))
            return;

        if (sourceJobIds is null || sourceJobIds.Count == 0)
            return;

        // Sin claim propio emitido en este ciclo no hay nada que confirmar (Fase 1.5).
        if (_lastClaimToken is null)
            return;

        var ids = sourceJobIds.Distinct().ToArray();
        var expectedToken = _lastClaimToken;
        var rows = await _dbContext.SourcePrintJobs
            .Where(x => ids.Contains(x.Id) && x.ClaimedBy == _workerId && x.ClaimToken == expectedToken)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.IsProcessed = true;
            row.ClaimedBy = null;
            row.ClaimedUntilUtc = null;
            row.ClaimToken = null;
        }

        var affected = await _dbContext.SaveChangesAsync(cancellationToken);
        if (rows.Count < sourceJobIds.Count)
        {
            _logger.LogWarning(
                "SapHana ack parcial: solicitados={Expected}, actualizados={Rows}, affected={Affected}, worker={WorkerId}",
                sourceJobIds.Count,
                rows.Count,
                affected,
                _workerId);
        }
    }

    public async Task RenewJobLeasesAsync(IReadOnlyList<long> sourceJobIds, CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.Mode, "SapHana", StringComparison.OrdinalIgnoreCase))
            return;

        if (sourceJobIds is null || sourceJobIds.Count == 0)
            return;

        // Sin claim propio emitido en este ciclo no hay nada que renovar (Fase 1.5).
        if (_lastClaimToken is null)
            return;

        var leaseSeconds = Math.Max(15, _hanaOptions.LeaseSeconds);
        var newExpiry = _timeProvider.GetUtcNow().AddSeconds(leaseSeconds);
        var ids = sourceJobIds.Distinct().ToArray();
        var expectedToken = _lastClaimToken;

        await _dbContext.SourcePrintJobs
            .Where(x => ids.Contains(x.Id) && x.ClaimedBy == _workerId && x.ClaimToken == expectedToken)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.ClaimedUntilUtc, newExpiry),
                cancellationToken);
    }
}
