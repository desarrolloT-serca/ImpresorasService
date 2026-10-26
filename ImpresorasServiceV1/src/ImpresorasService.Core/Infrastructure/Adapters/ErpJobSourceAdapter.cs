using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Models;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace ImpresorasService.Infrastructure.Adapters;

public class ErpJobSourceAdapter : IJobSourceAdapter
{
    private readonly SourceOptions _options;
    private readonly ImpresorasDbContext _dbContext;
    private readonly ILogger<ErpJobSourceAdapter> _logger;
    private readonly string _workerId;
    private readonly string _sourceSystem;

    // Token del último claim emitido por esta instancia (Scoped: una instancia = un ciclo de
    // polling = un FetchPendingJobsAsync seguido de su Mark/Renew). Exigirlo en ACK y renovación
    // (Fase 1.5) evita operar sobre una fila cuyo lease expiró y fue reclamada por otro worker.
    private string? _lastClaimToken;
    private readonly TimeProvider _timeProvider;

    private const int MaxPdfBytes = 50 * 1024 * 1024;

    // Uno por proceso: el adaptador es Scoped. Sin redirecciones, para no salir de PdfUrlHosts.
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AllowAutoRedirect = false
    }) { Timeout = TimeSpan.FromSeconds(30) };

    public ErpJobSourceAdapter(
        IOptions<SourceOptions> options,
        ImpresorasDbContext dbContext,
        ILogger<ErpJobSourceAdapter> logger,
        TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _options = options.Value;
        _dbContext = dbContext;
        _logger = logger;
        _workerId = $"{Environment.MachineName}-{Process.GetCurrentProcess().Id}";
        _sourceSystem = string.IsNullOrWhiteSpace(_options.SourceSystem) ? "ERP" : _options.SourceSystem.Trim();
    }

    public async Task<IReadOnlyList<IncomingPrintJob>> FetchPendingJobsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
            return Array.Empty<IncomingPrintJob>();

        var claimToken = Guid.NewGuid().ToString("N");
        var now = _timeProvider.GetUtcNow();
        var leaseSeconds = Math.Max(15, _options.LeaseSeconds);
        var leaseEnd = now.AddSeconds(leaseSeconds);

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Selección de candidatos: proyección escalar sin PdfBlob (Fase 1.3) y filtro de
        // disponibilidad de claim + orden empujados a SQL antes del Take (Fase 1.2). Antes,
        // Take(batchSize*5) cortaba por Id antes de filtrar por claim: si las primeras filas
        // estaban reclamadas por otro worker, se perdía el ciclo aunque hubiera libres después,
        // y además se cargaba el blob completo de filas que ni siquiera se iban a reclamar.
        var candidateIds = await _dbContext.SourcePrintJobs
            .Where(x => !x.IsProcessed
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
            .Where(x => candidateIds.Contains(x.Id) && !x.IsProcessed)
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

        var jobs = new List<IncomingPrintJob>(claimedRows.Count);
        foreach (var row in claimedRows.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id))
        {
            byte[]? pdf = row.PdfBlob is { Length: > 0 } ? row.PdfBlob : null;
            string? pdfError = null;

            if (pdf is null && !string.IsNullOrWhiteSpace(row.PdfUrl))
            {
                (pdf, pdfError, var retry) = await DownloadPdfAsync(Http, row.PdfUrl, _options.PdfUrlHosts, cancellationToken);
                if (retry)
                {
                    // Fallo pasajero (red, timeout, 5xx): sin ack; se reintenta cuando expire el lease.
                    _logger.LogWarning("Origen: no se pudo descargar el PDF de la fila {Id}; se reintentará. {Error}", row.Id, pdfError);
                    continue;
                }
            }
            else if (pdf is null)
            {
                // Una fila sin PDF ni URL no puede imprimirse. No deberia existir entre las no
                // procesadas -la retencion solo toca las que ya lo estan-, pero si aparece se
                // descarta con traza en vez de reventar el lote entero al calcular el hash.
                _logger.LogError("Origen: la fila {Id} no tiene PDF ni pdf_url pese a no estar procesada; se omite.", row.Id);
                continue;
            }

            jobs.Add(new IncomingPrintJob(
                SourceJobId: row.Id,
                SourceSystem: _sourceSystem,
                ExternalJobId: row.ExternalJobId,
                StoreId: row.StoreId,
                DocumentType: row.DocumentType,
                Channel: string.IsNullOrWhiteSpace(row.Channel) ? "DEFAULT" : row.Channel,
                PdfBlob: pdf ?? [],
                CreatedAtUtc: row.CreatedAtUtc,
                PdfError: pdfError));
        }

        _logger.LogInformation("Ingesta ERP: reclamados {Count} trabajos pendientes.", jobs.Count);
        return jobs;
    }

    /// <summary>
    /// Descarga el PDF de pdf_url. Retry=true: fallo pasajero, se vuelve a intentar más tarde; si no,
    /// el error es definitivo. Los mensajes no llevan la URL: con su id basta para bajarse el documento.
    /// </summary>
    public static async Task<(byte[]? Pdf, string? Error, bool Retry)> DownloadPdfAsync(
        HttpClient http, string url, IReadOnlyCollection<string> allowedHosts, CancellationToken ct)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return (null, "pdf_url no es una URL https.", false);
        if (!allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            return (null, $"El host {uri.Host} no está en Source:PdfUrlHosts.", false);

        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            var status = (int)response.StatusCode;
            if (status >= 500 || status is 408 or 429)
                return (null, $"{uri.Host} respondió {status}.", true);
            if (!response.IsSuccessStatusCode)
                return (null, $"{uri.Host} respondió {status}.", false);
            // ponytail: sin Content-Length el tope se mira después de leer; el host es nuestro.
            if (response.Content.Headers.ContentLength > MaxPdfBytes)
                return (null, $"El PDF de {uri.Host} pasa de {MaxPdfBytes / 1024 / 1024} MB.", false);

            var pdf = await response.Content.ReadAsByteArrayAsync(ct);
            if (pdf.Length > MaxPdfBytes)
                return (null, $"El PDF de {uri.Host} pasa de {MaxPdfBytes / 1024 / 1024} MB.", false);
            if (!pdf.AsSpan().StartsWith("%PDF-"u8))
                return (null, $"{uri.Host} no devolvió un PDF.", false);
            return (pdf, null, false);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            return (null, $"Sin respuesta de {uri.Host}: {ex.Message}", true);
        }
    }

    public async Task MarkJobsProcessedAsync(
        IReadOnlyList<long> sourceJobIds,
        CancellationToken cancellationToken)
    {
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
                "Ingesta ERP: ack parcial: solicitados={Expected}, actualizados={Rows}, affected={Affected}, worker={WorkerId}",
                sourceJobIds.Count,
                rows.Count,
                affected,
                _workerId);
        }
    }

    public async Task RenewJobLeasesAsync(IReadOnlyList<long> sourceJobIds, CancellationToken cancellationToken)
    {
        if (sourceJobIds is null || sourceJobIds.Count == 0)
            return;

        // Sin claim propio emitido en este ciclo no hay nada que renovar (Fase 1.5).
        if (_lastClaimToken is null)
            return;

        var leaseSeconds = Math.Max(15, _options.LeaseSeconds);
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
