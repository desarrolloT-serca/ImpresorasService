using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Worker;

public sealed class SpoolAcceptedWatchdogBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PrintExecutionOptions> _options;
    private readonly IIppConfirmationService _ippService;
    private readonly ILogger<SpoolAcceptedWatchdogBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;

    public SpoolAcceptedWatchdogBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<PrintExecutionOptions> options,
        IIppConfirmationService ippService,
        ILogger<SpoolAcceptedWatchdogBackgroundService> logger,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _ippService = ippService;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallo en watchdog de SpoolAccepted.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(Math.Max(1, _options.Value.SpoolAcceptedWatchIntervalSeconds)),
                stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var maxAgeSeconds = Math.Max(1, _options.Value.SpoolAcceptedMaxAgeSeconds);
        var thresholdUtc = now - TimeSpan.FromSeconds(maxAgeSeconds);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

        var batchSize = Math.Max(1, _options.Value.SpoolAcceptedWatchBatchSize);

        // Filtro de threshold y orden por antigüedad empujados a SQL (Fase 1.1): un Take en
        // memoria sin orden dejaba trabajos antiguos sin confirmar bajo backlog sostenido.
        // Proyectar solo campos escalares: PdfBlob se excluye deliberadamente para evitar OOM.
        var candidates = await db.PrintJobs
            .AsNoTracking()
            .Where(j => (j.Status == PrintJobStatus.SpoolAccepted || j.Status == PrintJobStatus.PrinterBlocked)
                && j.UpdatedAtUtc <= thresholdUtc)
            .OrderBy(j => j.UpdatedAtUtc)
            .Select(j => new WatchdogJob
            {
                JobId          = j.JobId,
                PrinterId      = j.PrinterId,
                Status         = j.Status,
                UpdatedAtUtc   = j.UpdatedAtUtc,
                LastErrorCode  = j.LastErrorCode,
                LastErrorMessage = j.LastErrorMessage,
                NextRetryAtUtc = j.NextRetryAtUtc,
            })
            .Take(batchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return;

        // Load printer hosts for IPP queries (one query per unique printer)
        var printerHosts = await LoadPrinterHostsAsync(db, candidates, ct);
        var ippResults = await QueryIppForPrintersAsync(printerHosts, ct);

        foreach (var job in candidates)
        {
            var oldStatus = job.Status;
            var ippResult = ResolveIppResult(job, printerHosts, ippResults);
            if (!ApplyOutcome(job, ippResult, maxAgeSeconds, now))
            {
                _logger.LogInformation(
                    "Watchdog: job {JobId} ({OldStatus}) pendiente — impresora {IppState} ({Age:F0} min).",
                    job.JobId, oldStatus, ippResult?.Outcome, (now - job.UpdatedAtUtc).TotalMinutes);
                continue;
            }

            // Attach + mark-modified evita cargar la entidad completa (PdfBlob).
            var entity = new PrintJob { JobId = job.JobId };
            db.PrintJobs.Attach(entity);
            entity.Status           = job.Status;
            entity.UpdatedAtUtc     = job.UpdatedAtUtc;
            entity.LastErrorCode    = job.LastErrorCode;
            entity.LastErrorMessage = job.LastErrorMessage;
            entity.NextRetryAtUtc  = job.NextRetryAtUtc;
            db.Entry(entity).Property(x => x.Status).IsModified           = true;
            db.Entry(entity).Property(x => x.UpdatedAtUtc).IsModified     = true;
            db.Entry(entity).Property(x => x.LastErrorCode).IsModified    = true;
            db.Entry(entity).Property(x => x.LastErrorMessage).IsModified = true;
            db.Entry(entity).Property(x => x.NextRetryAtUtc).IsModified   = true;

            await db.PrintJobEvents.AddAsync(BuildEvent(job, oldStatus, now), ct);
        }

        try
        {
            await db.SaveChangesAsync(ct);

            var confirmed = candidates.Count(j => j.Status == PrintJobStatus.PrintedConfirmed);
            var blocked   = candidates.Count(j => j.Status == PrintJobStatus.PrinterBlocked);
            var recovered = candidates.Count(j => j.Status == PrintJobStatus.SpoolAccepted);
            var unknown   = candidates.Count(j => j.Status == PrintJobStatus.PrintedUnknown);

            _logger.LogWarning(
                "Watchdog: {Total} jobs procesados — Confirmados={Confirmed} Bloqueados={Blocked} Recuperados={Recovered} Desconocidos={Unknown} (maxAge={MaxAge}s).",
                candidates.Count, confirmed, blocked, recovered, unknown, maxAgeSeconds);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogInformation("Watchdog: concurrencia detectada; reintento en el siguiente ciclo.");
        }
    }

    private static async Task<Dictionary<int, (string Host, bool? IppSupported)>> LoadPrinterHostsAsync(
        ImpresorasDbContext db,
        List<WatchdogJob> candidates,
        CancellationToken ct)
    {
        var printerIds = candidates
            .Where(j => j.PrinterId.HasValue)
            .Select(j => j.PrinterId!.Value)
            .Distinct()
            .ToList();

        if (printerIds.Count == 0)
            return [];

        var printers = await db.Printers
            .AsNoTracking()
            .Where(p => printerIds.Contains(p.PrinterId) && p.Host != null)
            .Select(p => new { p.PrinterId, p.Host, p.IppSupported })
            .ToListAsync(ct);

        return printers
            .Where(p => !string.IsNullOrWhiteSpace(p.Host))
            .ToDictionary(p => p.PrinterId, p => (p.Host!, p.IppSupported));
    }

    private async Task<Dictionary<string, IppQueryResult>> QueryIppForPrintersAsync(
        Dictionary<int, (string Host, bool? IppSupported)> printerHosts,
        CancellationToken ct)
    {
        if (!_options.Value.IppConfirmationEnabled || printerHosts.Count == 0)
            return [];

        // Saltarse impresoras ya conocidas como no-IPP
        var hostsToQuery = printerHosts.Values
            .Where(p => p.IppSupported != false)
            .Select(p => p.Host)
            .Distinct()
            .ToList();

        var tasks = hostsToQuery.Select(async host =>
            (host, result: await _ippService.QueryPrinterStateAsync(host, ct)));

        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(r => r.host, r => r.result);
    }

    private IppQueryResult? ResolveIppResult(
        WatchdogJob job,
        Dictionary<int, (string Host, bool? IppSupported)> printerHosts,
        Dictionary<string, IppQueryResult> ippResults)
    {
        if (!_options.Value.IppConfirmationEnabled) return null;
        if (!job.PrinterId.HasValue) return null;
        if (!printerHosts.TryGetValue(job.PrinterId.Value, out var printerInfo)) return null;
        if (!ippResults.TryGetValue(printerInfo.Host, out var result)) return null;
        return result;
    }

    // Tiempo máximo de espera para impresora detenida o procesando antes de desistir.
    private const int WaitGiveUpMinutes = 30;

    // Devuelve false si el trabajo debe ignorarse este ciclo (esperar recuperación de impresora).
    private static bool ApplyOutcome(
        WatchdogJob job,
        IppQueryResult? ippResult,
        int maxAgeSeconds,
        DateTimeOffset now)
    {
        var isBlocked = job.Status == PrintJobStatus.PrinterBlocked;

        switch (ippResult?.Outcome)
        {
            case IppOutcome.PrinterIdle:
                // Impresora libre: trabajo completado (aplica tanto a SpoolAccepted como a PrinterBlocked recuperada).
                job.NextRetryAtUtc = null;
                job.UpdatedAtUtc = now;
                job.Status = PrintJobStatus.PrintedConfirmed;
                job.LastErrorCode = null;
                job.LastErrorMessage = null;
                return true;

            case IppOutcome.PrinterProcessing:
                if (isBlocked)
                {
                    // Impresora se recuperó del bloqueo y está imprimiendo → volver a SpoolAccepted.
                    job.UpdatedAtUtc = now;
                    job.Status = PrintJobStatus.SpoolAccepted;
                    job.LastErrorCode = null;
                    job.LastErrorMessage = null;
                    return true;
                }
                // SpoolAccepted: impresora ocupada, esperar a que termine.
                if ((now - job.UpdatedAtUtc).TotalMinutes < WaitGiveUpMinutes)
                    return false;
                goto giveUp;

            case IppOutcome.PrinterStopped:
                if (!isBlocked)
                {
                    // SpoolAccepted → PrinterBlocked: visibilidad al usuario de que la impresora está detenida.
                    job.UpdatedAtUtc = now;
                    job.Status = PrintJobStatus.PrinterBlocked;
                    job.LastErrorCode = ippResult.ErrorCode ?? "IPP_PRINTER_STOPPED";
                    job.LastErrorMessage = ippResult.ErrorMessage ?? "Impresora detenida (sin papel, atasco u otro error).";
                    return true;
                }
                // Ya está bloqueado: seguir esperando recuperación.
                if ((now - job.UpdatedAtUtc).TotalMinutes < WaitGiveUpMinutes)
                    return false;
                goto giveUp;

            giveUp:
                job.NextRetryAtUtc = null;
                job.UpdatedAtUtc = now;
                job.Status = PrintJobStatus.PrintedUnknown;
                job.LastErrorCode = ippResult!.Outcome == IppOutcome.PrinterStopped
                    ? "IPP_PRINTER_STOPPED_TIMEOUT"
                    : "IPP_PRINTER_PROCESSING_TIMEOUT";
                job.LastErrorMessage =
                    $"Impresora en estado '{ippResult.Outcome}' durante más de {WaitGiveUpMinutes} min. Estado desconocido.";
                return true;

            default:
                // IPP no disponible: si el trabajo ya estaba bloqueado, esperar hasta el timeout.
                if (isBlocked && (now - job.UpdatedAtUtc).TotalMinutes < WaitGiveUpMinutes)
                    return false;
                job.NextRetryAtUtc = null;
                job.UpdatedAtUtc = now;
                job.Status = PrintJobStatus.PrintedUnknown;
                job.LastErrorCode = "SPOOL_ACCEPTED_TIMEOUT";
                job.LastErrorMessage = isBlocked
                    ? $"Impresora bloqueada sin respuesta IPP durante más de {WaitGiveUpMinutes} min. Estado desconocido."
                    : $"SpoolAccepted sin confirmación tras {maxAgeSeconds}s (watchdog).";
                return true;
        }
    }

    private static PrintJobEvent BuildEvent(WatchdogJob job, PrintJobStatus oldStatus, DateTimeOffset now)
    {
        var message = job.Status switch
        {
            PrintJobStatus.PrintedConfirmed => "IPP confirmó impresión completada.",
            PrintJobStatus.PrinterBlocked   => job.LastErrorMessage,
            PrintJobStatus.SpoolAccepted    => "Impresora recuperada, esperando confirmación.",
            _                               => job.LastErrorMessage
        };

        return new PrintJobEvent
        {
            JobId = job.JobId,
            EventType = "StatusChanged",
            OldStatus = oldStatus,
            NewStatus = job.Status,
            ErrorCode = job.LastErrorCode,
            Message = message,
            ActorType = "system",
            OccurredAtUtc = now
        };
    }

    private sealed class WatchdogJob
    {
        public Guid JobId { get; init; }
        public int? PrinterId { get; init; }
        public PrintJobStatus Status { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public string? LastErrorCode { get; set; }
        public string? LastErrorMessage { get; set; }
        public DateTimeOffset? NextRetryAtUtc { get; set; }
    }
}
