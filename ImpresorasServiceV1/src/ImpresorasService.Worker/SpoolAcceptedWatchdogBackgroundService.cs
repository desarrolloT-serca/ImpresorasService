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
    private readonly WorkerLockState _lockState;

    public SpoolAcceptedWatchdogBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<PrintExecutionOptions> options,
        IIppConfirmationService ippService,
        ILogger<SpoolAcceptedWatchdogBackgroundService> logger,
        TimeProvider timeProvider,
        WorkerLockState lockState)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _ippService = ippService;
        _logger = logger;
        _timeProvider = timeProvider;
        _lockState = lockState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // G4.1: sin el lock de instancia única, esta réplica no confirma/escribe eventos.
            if (!_lockState.IsHolder)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (FallosBd.EsFalloDeConexion(ex))
            {
                // La base no responde: lo anota EstadoBd, una vez y con el motivo.
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
                IppJobId       = j.IppJobId,
            })
            .Take(batchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return;

        // Load printer hosts for IPP queries (one query per unique printer)
        var printerHosts = await LoadPrinterHostsAsync(db, candidates, ct);
        var ippResults = await QueryIppForPrintersAsync(printerHosts, ct);

        // Solo los jobs que ApplyOutcome ha cambiado. Contar sobre `candidates` mezclaba los
        // ignorados con los procesados: "Recuperados" acababa contando los que seguían esperando.
        var changed = new List<(WatchdogJob Job, PrintJobStatus OldStatus)>();

        foreach (var job in candidates)
        {
            var oldStatus = job.Status;
            // Lo que salió por IP se confirma por su número de trabajo; si la impresora ya no lo
            // recuerda, por su estado, como lo que salió por la cola de Windows.
            var ippResult = await QueryJobAsync(job, printerHosts, ct)
                ?? ResolveIppResult(job, printerHosts, ippResults);
            if (!ApplyOutcome(job, ippResult, maxAgeSeconds, now))
            {
                _logger.LogInformation(
                    "Watchdog: job {JobId} ({OldStatus}) pendiente — impresora {IppState} ({Age:F0} min).",
                    job.JobId, oldStatus, ippResult?.Outcome, (now - job.UpdatedAtUtc).TotalMinutes);
                continue;
            }

            // Sin Cancel-Job automático (decisión del usuario, 27/10/2026, cambia D3): un trabajo que
            // la impresora tiene parado demasiado tiempo queda «Sin confirmar, puede salir aún» y lo
            // resuelve una persona desde la cola, con su evento. Cancelar por número podía acertar a
            // otro trabajo si la impresora se reinició y reutiliza números (3b.8): un error oculto.

            changed.Add((job, oldStatus));
        }

        if (changed.Count == 0)
            return;

        // Cada cambio, condicionado al estado que se leyó: si un operador lo canceló (o cambió) mientras
        // se consultaban las impresoras, su decisión manda y aquí no se pisa (auditoría L1). En una
        // transacción con sus eventos, y después de las consultas para no tenerla abierta durante la red.
        var applied = new List<WatchdogJob>();
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            foreach (var (job, oldStatus) in changed)
            {
                var rows = await db.PrintJobs
                    .Where(j => j.JobId == job.JobId && j.Status == oldStatus)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, job.Status)
                        .SetProperty(x => x.UpdatedAtUtc, job.UpdatedAtUtc)
                        .SetProperty(x => x.LastErrorCode, job.LastErrorCode)
                        .SetProperty(x => x.LastErrorMessage, job.LastErrorMessage)
                        .SetProperty(x => x.NextRetryAtUtc, job.NextRetryAtUtc), ct);
                if (rows == 0)
                    continue;
                db.PrintJobEvents.Add(BuildEvent(job, oldStatus, now));
                applied.Add(job);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var confirmed = applied.Count(j => j.Status == PrintJobStatus.PrintedConfirmed);
        var blocked   = applied.Count(j => j.Status == PrintJobStatus.PrinterBlocked);
        var recovered = applied.Count(j => j.Status == PrintJobStatus.SpoolAccepted);
        var unknown   = applied.Count(j => j.Status == PrintJobStatus.PrintedUnknown);

        _logger.LogInformation(
            "Watchdog: {Changed}/{Total} jobs actualizados — Confirmados={Confirmed} Bloqueados={Blocked} Recuperados={Recovered} Desconocidos={Unknown} (maxAge={MaxAge}s).",
            applied.Count, candidates.Count, confirmed, blocked, recovered, unknown, maxAgeSeconds);
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

    private async Task<IppQueryResult?> QueryJobAsync(
        WatchdogJob job,
        Dictionary<int, (string Host, bool? IppSupported)> printerHosts,
        CancellationToken ct)
    {
        if (!_options.Value.IppConfirmationEnabled || job.IppJobId is null || !job.PrinterId.HasValue)
            return null;
        return printerHosts.TryGetValue(job.PrinterId.Value, out var printerInfo)
            ? await _ippService.QueryJobStateAsync(printerInfo.Host, job.IppJobId.Value, ct)
            : null;
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

    // Tiempo máximo de espera con la impresora procesando antes de desistir.
    private const int WaitGiveUpMinutes = 30;
    // Con la impresora parada (sin papel, atasco), menos: son pedidos de clientes y urgen (D3, 27/10/2026).
    private const int BlockedGiveUpMinutes = 10;

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
                if ((now - job.UpdatedAtUtc).TotalMinutes < BlockedGiveUpMinutes)
                    return false;
                goto giveUp;

            case IppOutcome.CompletedWithErrors:
                // La impresora lo dio por terminado con errores: puede no haber salido bien.
                job.NextRetryAtUtc = null;
                job.UpdatedAtUtc = now;
                job.Status = PrintJobStatus.PrintedUnknown;
                job.LastErrorCode = ippResult.ErrorCode ?? "IPP_COMPLETED_WITH_ERRORS";
                job.LastErrorMessage = ippResult.ErrorMessage;
                return true;

            case IppOutcome.JobAborted:
                // La impresora canceló o abortó el trabajo enviado por IP: no salió.
                job.NextRetryAtUtc = null;
                job.UpdatedAtUtc = now;
                job.Status = PrintJobStatus.ErrorFinal;
                job.LastErrorCode = ippResult.ErrorCode ?? "IPP_JOB_ABORTED";
                job.LastErrorMessage = ippResult.ErrorMessage ?? "La impresora abortó el trabajo.";
                return true;

            giveUp:
                job.NextRetryAtUtc = null;
                job.UpdatedAtUtc = now;
                job.Status = PrintJobStatus.PrintedUnknown;
                job.LastErrorCode = ippResult!.Outcome == IppOutcome.PrinterStopped
                    ? "IPP_PRINTER_STOPPED_TIMEOUT"
                    : "IPP_PRINTER_PROCESSING_TIMEOUT";
                job.LastErrorMessage = ippResult.Outcome == IppOutcome.PrinterStopped
                    ? $"Impresora parada más de {BlockedGiveUpMinutes} min: puede salir aún cuando se recupere."
                    : $"Impresora ocupada más de {WaitGiveUpMinutes} min sin terminarlo: puede salir aún.";
                return true;

            default:
                // IPP no disponible: si el trabajo ya estaba bloqueado, esperar hasta el timeout.
                if (isBlocked && (now - job.UpdatedAtUtc).TotalMinutes < BlockedGiveUpMinutes)
                    return false;
                job.NextRetryAtUtc = null;
                job.UpdatedAtUtc = now;
                job.Status = PrintJobStatus.PrintedUnknown;
                job.LastErrorCode = "SPOOL_ACCEPTED_TIMEOUT";
                job.LastErrorMessage = isBlocked
                    ? $"Impresora bloqueada sin respuesta IPP durante más de {BlockedGiveUpMinutes} min. Estado desconocido."
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
        public int? IppJobId { get; init; }
    }
}
