using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// Ejecución de la cola contra el spooler.
///
/// <para><b>Exclusión entre procesos (Fase 2.2).</b> Todas las transiciones de estado de este
/// servicio se aplican con un UPDATE condicionado al estado esperado (<see cref="TryTransitionAsync"/>):
/// leer y escribir son el mismo statement, así que de dos procesos que lleguen a la vez uno afecta a
/// 1 fila y el otro a 0. La que importa de verdad es el paso a <c>Printing</c>: es lo único que
/// impide que dos Workers manden el mismo documento al spooler y salga el papel dos veces.</para>
///
/// <para>Antes esto era leer-comprobar-escribir con un snapshot de <c>RowVersion</c>, que dejaba
/// abierta la ventana entre la lectura y el guardado. <c>RowVersion</c> además nunca fue un token de
/// concurrencia de EF: en HANA es un BLOB y no se puede comparar en un WHERE.</para>
/// </summary>
public sealed class PrintExecutionService : IPrintExecutionService
{
    private readonly ImpresorasDbContext _db;
    private readonly IPrinterSpooler _spooler;
    private readonly ILogger<PrintExecutionService> _logger;
    private readonly PrintExecutionOptions _options;
    private readonly IRoutingResolver _routingResolver;
    private readonly TimeProvider _timeProvider;

    public PrintExecutionService(
        ImpresorasDbContext db,
        IPrinterSpooler spooler,
        ILogger<PrintExecutionService> logger,
        IOptions<PrintExecutionOptions> options,
        IRoutingResolver routingResolver,
        TimeProvider timeProvider)
    {
        _db = db;
        _spooler = spooler;
        _logger = logger;
        _options = options.Value;
        _routingResolver = routingResolver;
        _timeProvider = timeProvider;
    }

    public async Task<int> ExecuteBatchAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        // "Printing" con más antigüedad que el timeout + buffer es un envío que quedó sin resolver.
        // Se recoge aquí para cerrarlo como PrintedUnknown (ver TryProcessOneAsync), NO para
        // reenviarlo: no hay forma de saber si el papel salió y un reenvío duplicaría el pedido.
        // El buffer evita tocar jobs cuyo spooler todavía está trabajando.
        var stalePrintingAfter = TimeSpan.FromSeconds(_options.TimeoutSeconds + 10);
        // Pending >2 min = IngestionService falló al enrutar; rescatar aquí para evitar huérfanos indefinidos.
        var stalePendingAfter = TimeSpan.FromMinutes(2);
        // Solo traer trabajos realmente elegibles. Si hacemos Take antes de comprobar NextRetryAtUtc,
        // muchos RetryScheduled todavia no vencidos pueden ocupar la ventana y dejar fuera reintentos listos.
        var eligible = await _db.PrintJobs
            .AsNoTracking()
            .Where(j =>
                j.Status == PrintJobStatus.Routed
                || (j.Status == PrintJobStatus.RetryScheduled && j.NextRetryAtUtc != null && j.NextRetryAtUtc <= now)
                || (j.Status == PrintJobStatus.Printing && j.UpdatedAtUtc <= now - stalePrintingAfter)
                || (j.Status == PrintJobStatus.Pending && j.UpdatedAtUtc <= now - stalePendingAfter))
            .OrderBy(j => j.NextRetryAtUtc ?? j.CreatedAtUtc)
            .ThenBy(j => j.CreatedAtUtc)
            .Take(batchSize)
            .Select(j => new
            {
                j.JobId,
                j.PrinterId,
                j.Status,
                j.StoreId,
                j.DocumentType,
                j.Channel
            })
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var item in eligible)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Rescate de Pending huérfanos: la ingesta los insertó pero el routing lanzó excepción.
            if (item.Status == PrintJobStatus.Pending)
            {
                if (await RescuePendingJobAsync(item.JobId, item.StoreId, item.DocumentType, item.Channel, cancellationToken))
                    processed++;
                continue;
            }

            int printerIdToUse;
            if (item.PrinterId != null)
            {
                printerIdToUse = item.PrinterId.Value;
            }
            else
            {
                // Auto-curación: si el job está en Routed pero sin PrinterId, intentamos resolverlo.
                var resolved = await _routingResolver.ResolvePrinterAsync(
                    item.StoreId,
                    item.DocumentType,
                    item.Channel,
                    cancellationToken);

                if (resolved is null)
                {
                    if (await TryTransitionAsync(
                            item.JobId,
                            [item.Status],
                            PrintJobStatus.ErrorFinal,
                            "ROUTE_NOT_FOUND",
                            "No existe regla activa aplicable para este trabajo (PrinterId nulo).",
                            nextRetryAtUtc: null,
                            newAttemptCount: null,
                            cancellationToken))
                        processed++;

                    continue;
                }

                printerIdToUse = resolved.Value;
            }

            var ok = await TryProcessOneAsync(item.JobId, printerIdToUse, cancellationToken);
            if (ok) processed++;
        }

        return processed;
    }

    private async Task<bool> TryProcessOneAsync(Guid jobId, int printerId, CancellationToken ct)
    {
        // AsNoTracking en todo el método: las transiciones van por ExecuteUpdate, que no refresca las
        // entidades cargadas. Una entidad seguida se quedaría con los valores de antes del UPDATE y
        // el siguiente `FirstOrDefaultAsync` devolvería esa copia rancia en vez de leer la fila.
        var job = await _db.PrintJobs.AsNoTracking().FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (job == null) return false;

        var printer = await _db.Printers.AsNoTracking().FirstOrDefaultAsync(p => p.PrinterId == printerId, ct);
        if (printer == null || !printer.IsActive)
        {
            return await TryTransitionAsync(
                jobId, [job.Status], PrintJobStatus.ErrorFinal,
                "PRINTER_INVALID", "Impresora inactiva o inexistente",
                nextRetryAtUtc: null, newAttemptCount: null, ct);
        }

        // Guard-rail: si el monitor de conectividad marca KO reciente, evitamos
        // enviar al spooler porque puede devolver "aceptado" aunque el dispositivo
        // fisico esté apagado/no alcanzable.
        if (printer.LastConnectionOk == false && printer.LastConnectionCheckAtUtc.HasValue)
        {
            var lastCheckAge = _timeProvider.GetUtcNow() - printer.LastConnectionCheckAtUtc.Value;
            if (lastCheckAge <= TimeSpan.FromSeconds(90))
            {
                var unreachableMessage = printer.LastConnectionError ?? "Impresora no alcanzable (monitor de conectividad).";
                var nextAttempt = job.AttemptCount + 1;

                if (nextAttempt < _options.MaxAttempts)
                {
                    var delaySec = _options.BackoffSeconds[Math.Min(nextAttempt - 1, _options.BackoffSeconds.Length - 1)];
                    return await TryTransitionAsync(
                        jobId, [job.Status], PrintJobStatus.RetryScheduled,
                        "PRINTER_UNREACHABLE", unreachableMessage,
                        nextRetryAtUtc: _timeProvider.GetUtcNow().AddSeconds(delaySec),
                        newAttemptCount: nextAttempt, ct);
                }

                return await TryTransitionAsync(
                    jobId, [job.Status], PrintJobStatus.ErrorFinal,
                    "PRINTER_UNREACHABLE", unreachableMessage,
                    nextRetryAtUtc: null, newAttemptCount: nextAttempt, ct);
            }
        }

        var stalePrintingAfter = TimeSpan.FromSeconds(_options.TimeoutSeconds + 10);
        if (job.Status == PrintJobStatus.Printing)
        {
            // Fresco: el envío al spooler sigue en curso, no tocar.
            if (job.UpdatedAtUtc > _timeProvider.GetUtcNow() - stalePrintingAfter)
                return false;

            // Stale: el proceso murió entre el COMMIT de Printing y el resultado del spooler.
            // La BD y la impresora no comparten transacción, así que aquí es imposible saber si
            // el papel llegó a salir. Reenviarlo automáticamente sacaría un segundo documento
            // cada vez que el envío anterior sí había prosperado.
            // Decisión de negocio: parar antes que duplicar. Se marca la incertidumbre y un
            // operador resuelve desde la cola (reimprimir si no salió, o cancelar).
            var closed = await TryTransitionAsync(
                jobId, [PrintJobStatus.Printing], PrintJobStatus.PrintedUnknown,
                "PRINTING_INTERRUPTED",
                "El envío a la impresora se interrumpió sin conocerse el resultado. Puede haberse impreso: compruébelo antes de reimprimir.",
                nextRetryAtUtc: null, newAttemptCount: null, ct);

            if (closed)
                _logger.LogWarning(
                    "JobId={JobId}: Printing sin resolver tras {Seconds}s. Marcado PrintedUnknown; requiere decisión manual (no se reenvía para no duplicar).",
                    jobId, stalePrintingAfter.TotalSeconds);

            return closed;
        }

        if (job.Status != PrintJobStatus.Routed && job.Status != PrintJobStatus.RetryScheduled)
            return false;

        if (job.Status == PrintJobStatus.RetryScheduled && job.NextRetryAtUtc > _timeProvider.GetUtcNow())
            return false;

        if (job.AttemptCount >= _options.MaxAttempts)
        {
            return await TryTransitionAsync(
                jobId, [job.Status], PrintJobStatus.ErrorFinal,
                "RETRIES_EXHAUSTED", "Intentos agotados",
                nextRetryAtUtc: null, newAttemptCount: null, ct);
        }

        // CLAIM ATÓMICO. El estado en el WHERE es la exclusión: si otro proceso llegó primero, este
        // ve 0 filas afectadas y no envía nada. AttemptCount se fija a un valor absoluto y no a
        // "+1" a propósito: si alguien lo hubiera incrementado entre la lectura y ahora, también
        // habría cambiado el estado y el WHERE ya no casaría.
        var claimed = await TryTransitionAsync(
            jobId,
            [PrintJobStatus.Routed, PrintJobStatus.RetryScheduled],
            PrintJobStatus.Printing,
            errorCode: null, errorMessage: null,
            nextRetryAtUtc: null,
            newAttemptCount: job.AttemptCount + 1,
            ct);

        if (!claimed)
        {
            _logger.LogDebug("JobId={JobId}: otro proceso lo reclamó primero; este ciclo no lo envía.", jobId);
            return false;
        }

        PrintSpoolResult result;
        try
        {
            if (job.PdfBlob is null || job.PdfBlob.Length == 0)
                result = new PrintSpoolResult(false, "PDF_MISSING", "PDF no disponible en la base de datos.", false);
            else
                result = await _spooler.SendToPrinterAsync(job.PdfBlob, printer.SpoolQueue, ct);
        }
        catch (OperationCanceledException)
        {
            // NO transitorio: ver la nota de WindowsPrintSpooler. El envío estaba en curso cuando
            // venció el plazo, así que no sabemos si salió el papel.
            result = new PrintSpoolResult(false, "NET_TIMEOUT", "Timeout de impresión", false);
        }
        catch (Exception ex)
        {
            // Nota de decisión: no volcamos ex.Message completo al estado final para evitar
            // que información interna se propague a UI/logs. Guardamos un mensaje genérico.
            _logger.LogError(ex, "Excepcion enviando a spooler. JobId={JobId} Printer={PrinterId}", jobId, printerId);
            result = new PrintSpoolResult(false, "SPOOLER_EXCEPTION", "Error en spooler", true);
        }

        // Todas las salidas exigen seguir en Printing: si un operador canceló mientras el spooler
        // trabajaba, su decisión manda y aquí no se pisa.
        if (result.Success)
        {
            await TryTransitionAsync(
                jobId, [PrintJobStatus.Printing], PrintJobStatus.SpoolAccepted,
                errorCode: null, errorMessage: null,
                nextRetryAtUtc: null, newAttemptCount: null, ct,
                eventMessage: "Spooler aceptó el trabajo");

            return true;
        }

        if (result.ErrorCode == "NET_TIMEOUT")
        {
            // El envío se cortó a medias: pudo haber llegado a la cola de Windows. Ni error (afirmaría
            // que no se imprimió) ni reintento (duplicaría). Mismo tratamiento que el Printing stale:
            // se marca la incertidumbre y decide un operador desde la cola.
            await TryTransitionAsync(
                jobId, [PrintJobStatus.Printing], PrintJobStatus.PrintedUnknown,
                result.ErrorCode,
                "El envío a la impresora se agotó de tiempo sin conocerse el resultado. Puede haberse impreso: compruébelo antes de reimprimir.",
                nextRetryAtUtc: null, newAttemptCount: null, ct);

            _logger.LogWarning(
                "JobId={JobId}: timeout de impresión. Marcado PrintedUnknown; requiere decisión manual (no se reenvía para no duplicar).",
                jobId);

            return true;
        }

        var attemptCount = job.AttemptCount + 1;
        if (result.IsTransient && attemptCount < _options.MaxAttempts)
        {
            var delaySec = _options.BackoffSeconds[Math.Min(attemptCount - 1, _options.BackoffSeconds.Length - 1)];
            await TryTransitionAsync(
                jobId, [PrintJobStatus.Printing], PrintJobStatus.RetryScheduled,
                result.ErrorCode, result.ErrorMessage,
                nextRetryAtUtc: _timeProvider.GetUtcNow().AddSeconds(delaySec),
                newAttemptCount: null, ct);

            return true;
        }

        var errCode = result.ErrorCode ?? "UNKNOWN";
        var errMsg = result.ErrorMessage ?? "Error desconocido";
        _logger.LogWarning("Impresion fallida JobId={JobId} Printer={Printer} Code={Code} Msg={Msg}",
            jobId, printer.SpoolQueue, errCode, errMsg);

        await TryTransitionAsync(
            jobId, [PrintJobStatus.Printing], PrintJobStatus.ErrorFinal,
            errCode, errMsg, nextRetryAtUtc: null, newAttemptCount: null, ct);

        return true;
    }

    private async Task<bool> RescuePendingJobAsync(
        Guid jobId, int storeId, string documentType, string channel, CancellationToken ct)
    {
        var resolved = await _routingResolver.ResolvePrinterAsync(storeId, documentType, channel, ct);

        if (resolved is null)
        {
            return await TryTransitionAsync(
                jobId, [PrintJobStatus.Pending], PrintJobStatus.ErrorFinal,
                "ROUTE_NOT_FOUND", "No existe regla activa aplicable para este trabajo.",
                nextRetryAtUtc: null, newAttemptCount: null, ct);
        }

        var routed = await TryTransitionAsync(
            jobId, [PrintJobStatus.Pending], PrintJobStatus.Routed,
            errorCode: null, errorMessage: null,
            nextRetryAtUtc: null, newAttemptCount: null, ct,
            eventMessage: $"Re-enrutado (rescate de Pending huérfano) a impresora {resolved}.",
            eventType: "ROUTED",
            printerId: resolved);

        if (routed)
            _logger.LogInformation("Rescate Pending: job {JobId} enrutado a impresora {PrinterId}.", jobId, resolved);

        return routed;
    }

    /// <summary>
    /// Aplica una transición solo si el trabajo sigue en uno de los estados esperados, y registra el
    /// evento correspondiente. Devuelve <c>true</c> si la fila se modificó.
    ///
    /// <para>El estado en el WHERE es lo que da la exclusión mutua entre procesos: la lectura y la
    /// escritura ocurren en el mismo statement, así que no hay ventana en la que otro Worker pueda
    /// colarse. Sustituye al antiguo chequeo de <c>RowVersion</c>, que comparaba en memoria un valor
    /// leído antes y por tanto no excluía nada.</para>
    /// </summary>
    private async Task<bool> TryTransitionAsync(
        Guid jobId,
        PrintJobStatus[] expectedStatuses,
        PrintJobStatus newStatus,
        string? errorCode,
        string? errorMessage,
        DateTimeOffset? nextRetryAtUtc,
        int? newAttemptCount,
        CancellationToken ct,
        string? eventMessage = null,
        string eventType = "StatusChanged",
        int? printerId = null)
    {
        var now = _timeProvider.GetUtcNow();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // El estado anterior se lee dentro de la transacción solo para el evento; quién puede
        // escribir lo decide el WHERE del UPDATE, no esta lectura.
        var oldStatus = await _db.PrintJobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => (PrintJobStatus?)j.Status)
            .FirstOrDefaultAsync(ct);

        var rows = await ApplyUpdateAsync();
        if (rows != 1)
        {
            await tx.CommitAsync(ct);
            return false;
        }

        await _db.PrintJobEvents.AddAsync(new PrintJobEvent
        {
            JobId = jobId,
            EventType = eventType,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            ErrorCode = errorCode,
            Message = eventMessage ?? errorMessage,
            ActorType = "system",
            OccurredAtUtc = now
        }, ct);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;

        // Dos variantes en vez de un setter condicional: EF traduce cada SetProperty a SQL, y meter
        // ahí un ?? sobre un valor capturado es pedirle una traducción que no necesita existir.
        Task<int> ApplyUpdateAsync()
        {
            // Comparaciones escalares y no expectedStatuses.Contains(...): Status se persiste como
            // texto (HasConversion<string>), y el IN generado a partir de la lista no aplicaba el
            // conversor — el UPDATE no casaba con ninguna fila y toda transición se perdía en
            // silencio. Ningún caso necesita más de dos estados esperados.
            var expected1 = expectedStatuses[0];
            var expected2 = expectedStatuses.Length > 1 ? expectedStatuses[1] : expected1;

            var query = _db.PrintJobs
                .Where(j => j.JobId == jobId && (j.Status == expected1 || j.Status == expected2));

            if (newAttemptCount.HasValue && printerId.HasValue)
                return query.ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, newStatus)
                    .SetProperty(x => x.LastErrorCode, errorCode)
                    .SetProperty(x => x.LastErrorMessage, errorMessage)
                    .SetProperty(x => x.NextRetryAtUtc, nextRetryAtUtc)
                    .SetProperty(x => x.AttemptCount, newAttemptCount.Value)
                    .SetProperty(x => x.PrinterId, printerId)
                    .SetProperty(x => x.UpdatedAtUtc, now), ct);

            if (newAttemptCount.HasValue)
                return query.ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, newStatus)
                    .SetProperty(x => x.LastErrorCode, errorCode)
                    .SetProperty(x => x.LastErrorMessage, errorMessage)
                    .SetProperty(x => x.NextRetryAtUtc, nextRetryAtUtc)
                    .SetProperty(x => x.AttemptCount, newAttemptCount.Value)
                    .SetProperty(x => x.UpdatedAtUtc, now), ct);

            if (printerId.HasValue)
                return query.ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, newStatus)
                    .SetProperty(x => x.LastErrorCode, errorCode)
                    .SetProperty(x => x.LastErrorMessage, errorMessage)
                    .SetProperty(x => x.NextRetryAtUtc, nextRetryAtUtc)
                    .SetProperty(x => x.PrinterId, printerId)
                    .SetProperty(x => x.UpdatedAtUtc, now), ct);

            return query.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, newStatus)
                .SetProperty(x => x.LastErrorCode, errorCode)
                .SetProperty(x => x.LastErrorMessage, errorMessage)
                .SetProperty(x => x.NextRetryAtUtc, nextRetryAtUtc)
                .SetProperty(x => x.UpdatedAtUtc, now), ct);
        }
    }
}
