using System.Security.Claims;
using System.Text.Json;
using ImpresorasService.Api.Security;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Authorize(Policy = "EmployeeOrAbove")]
[Route("api/[controller]")]
public class PrintJobsController : ControllerBase
{
    private readonly ImpresorasDbContext _dbContext;
    private readonly IRoutingService _routingService;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _businessTimeZone;
    private readonly IIppConfirmationService? _ipp;

    public PrintJobsController(ImpresorasDbContext dbContext, IRoutingService routingService, TimeProvider timeProvider,
        // ponytail: opcional para no tocar las pruebas que crean el controlador; en la app siempre está.
        IConfiguration? configuration = null,
        IIppConfirmationService? ipp = null)
    {
        _ipp = ipp;
        _dbContext = dbContext;
        _routingService = routingService;
        _timeProvider = timeProvider;
        // El mismo reloj de negocio que el dashboard: el registro de reimpresiones cuadra con su KPI.
        _businessTimeZone = BusinessTimeZoneClock.Resolve(configuration?["Dashboard:BusinessTimeZone"]);
    }

    [HttpGet]
    public async Task<IActionResult> GetQueue(
        [FromQuery] int? storeId,
        [FromQuery] PrintJobStatus? status,
        [FromQuery] int? limit,
        [FromQuery] int? page,
        [FromQuery] string? externalJobId,
        [FromQuery] bool? includeTotal,
        [FromQuery] bool? failedWithoutRetry,
        [FromQuery] bool? unassigned,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] string? printerIds,
        [FromQuery] string? documentTypes,
        [FromQuery] string? marks,
        CancellationToken cancellationToken)
    {
        // Búsqueda avanzada (27/10/2026). Listas separadas por comas, como las manda la web.
        int[]? impresoras = null;
        if (!string.IsNullOrWhiteSpace(printerIds))
        {
            var partes = printerIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            impresoras = partes.Select(p => int.TryParse(p, out var id) ? id : -1).ToArray();
            if (impresoras.Any(id => id < 0) || impresoras.Length > 100)
                return BadRequest(new { error = "printerIds: números de impresora separados por comas (100 como mucho)." });
        }
        var tipos = string.IsNullOrWhiteSpace(documentTypes)
            ? null
            : documentTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tipos?.Length > 20)
            return BadRequest(new { error = "documentTypes: 20 tipos como mucho." });
        // Marcas de la cola (ERP_SERCA, 11/11/2026; sustituye al registro de reimpresiones): cualquiera de ellas.
        var marcas = string.IsNullOrWhiteSpace(marks)
            ? null
            : marks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        if (marcas?.Except(Marcas).Any() == true)
            return BadRequest(new { error = $"marks: una o varias de {string.Join(", ", Marcas)}." });
        if (fromDate > toDate)
            return BadRequest(new { error = "La fecha desde no puede ser posterior a la fecha hasta." });

        IQueryable<PrintJob> query = _dbContext.PrintJobs.AsNoTracking();
        // Quién desvió, como en el detalle: solo quien puede verlo.
        var verQuien = User.VeQuien();

        var effectiveStoreId = User.VeTodasLasTiendas() ? storeId : GetCurrentUserStoreId();
        if (!User.VeTodasLasTiendas() && !effectiveStoreId.HasValue)
            return Forbid();
        if (effectiveStoreId.HasValue)
            query = query.Where(x => x.StoreId == effectiveStoreId.Value);

        // Mismo predicado que el KPI "sin reenviar" del dashboard, no una copia: así el número que
        // muestra el dashboard siempre se puede auditar listando exactamente esos trabajos. Con el
        // filtro por Status suelto no coincidían (un ErrorFinal es solo parte del conjunto).
        if (failedWithoutRetry == true)
        {
            query = query.Where(DashboardPrintJobPredicates.FailedWithoutRetryCurrent);
        }
        else if (unassigned == true)
        {
            // Mismo conjunto que unassignedQueueCurrent del dashboard: en cola Y sin impresora. Fija
            // sus propios estados, por eso va en la cascada excluyente y no como filtro adicional.
            query = query.Where(x => x.PrinterId == null
                                     && DashboardPrintJobPredicates.QueueStatuses.Contains(x.Status));
        }
        else if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(externalJobId))
        {
            var needle = externalJobId.Trim();
            query = query.Where(x => EF.Functions.Like(x.ExternalJobId, $"%{needle}%"));
        }

        if (impresoras is not null)
            query = query.Where(x => x.PrinterId.HasValue && impresoras.Contains(x.PrinterId.Value));
        if (tipos is not null)
            query = query.Where(x => tipos.Contains(x.DocumentType));
        if (marcas is not null)
        {
            bool Con(string m) => marcas.Contains(m);
            var (reserva, copia, reimpreso, reintento) = (Con("reserva"), Con("copia"), Con("reimpreso"), Con("reintento"));
            query = query.Where(x =>
                (reserva && x.DivertedFromPrinterId != null)
                || (copia && _dbContext.PrintJobEvents.Any(e => e.JobId == x.JobId
                    && e.EventType == RoutingService.ReprintEvent && e.OldStatus == PrintJobStatus.PrintedUnknown))
                || (reimpreso && _dbContext.PrintJobEvents.Any(e => e.JobId == x.JobId
                    && e.EventType == RoutingService.ReprintEvent && e.OldStatus != PrintJobStatus.PrintedUnknown))
                || (reintento && _dbContext.PrintJobEvents.Any(e => e.JobId == x.JobId
                    && e.EventType == RoutingService.RetryRouteEvent)));
        }
        // Días enteros de la zona de negocio, sobre la misma hora por la que se ordena (la última
        // vez que se mandó imprimir): lo que se ve en «Hora» es lo que se filtra.
        if (fromDate is DateOnly desde)
        {
            var inicio = BusinessTimeZoneClock.DayStartUtc(desde, _businessTimeZone);
            query = query.Where(x => (x.LastRequeuedAtUtc ?? x.CreatedAtUtc) >= inicio);
        }
        if (toDate is DateOnly hasta)
        {
            var fin = BusinessTimeZoneClock.DayStartUtc(hasta.AddDays(1), _businessTimeZone);
            query = query.Where(x => (x.LastRequeuedAtUtc ?? x.CreatedAtUtc) < fin);
        }

        // Protege la UI de cargas masivas accidentales (polling + tablas grandes).
        var safeLimit = Math.Clamp(limit ?? 100, 1, 500);
        var safePage = Math.Clamp(page ?? 1, 1, int.MaxValue / safeLimit); // evita overflow en Skip

        var projectedQuery =
            from job in query
            join printer in _dbContext.Printers.AsNoTracking()
                on job.PrinterId equals (int?)printer.PrinterId into printerJoin
            from printer in printerJoin.DefaultIfEmpty()
            // Por la última vez que se mandó imprimir: un reintento o una reimpresión sube arriba
            // aunque aún no haya salido, y no se pierde entre lo que llegó después (27/10/2026).
            // Mismo COALESCE que el índice ix_printer_print_job_orden_cola.
            orderby (job.LastRequeuedAtUtc ?? job.CreatedAtUtc) descending
            select new
            {
                job.JobId,
                job.ExternalJobId,
                job.StoreId,
                job.PrinterId,
                PrinterName = printer == null ? null : printer.PrinterName,
                job.DocumentType,
                job.Status,
                job.AttemptCount,
                job.LastErrorCode,
                job.LastErrorMessage,
                // El código, para la gente (ErroresTrabajo): lo mismo en el ERP, el PHP y la web.
                LastErrorText = ErroresTrabajo.Texto(job.LastErrorCode),
                LastErrorHint = ErroresTrabajo.QueHacer(job.LastErrorCode),
                job.CreatedAtUtc,
                job.UpdatedAtUtc,
                job.LastRequeuedAtUtc,
                // Reintentar o reimprimir reutiliza el trabajo: CreatedAtUtc es cuándo llegó y
                // SentAtUtc, cuándo salió hacia la impresora después del último reintento (null si aún
                // no ha vuelto a salir). Del log de eventos, sin columna propia.
                // Desde Printing: el envío; desde PrinterBlocked es «impresora recuperada» (auditoría L2).
                SentAtUtc = _dbContext.PrintJobEvents
                    .Where(e => e.JobId == job.JobId && e.NewStatus == PrintJobStatus.SpoolAccepted
                        && e.OldStatus == PrintJobStatus.Printing
                        && !_dbContext.PrintJobEvents.Any(r => r.JobId == job.JobId && r.EventId > e.EventId
                            && (r.EventType == RoutingService.RetryRouteEvent || r.EventType == RoutingService.ReprintEvent)))
                    .OrderByDescending(e => e.EventId)
                    .Select(e => (DateTimeOffset?)e.OccurredAtUtc)
                    .FirstOrDefault(),
                // Reintento: no había salido. Reimpresión: ya había salido, con o sin
                // confirmación, y la copia puede ser un duplicado.
                Retries = _dbContext.PrintJobEvents.Count(e => e.JobId == job.JobId
                    && e.EventType == RoutingService.RetryRouteEvent),
                Reprints = _dbContext.PrintJobEvents.Count(e => e.JobId == job.JobId
                    && e.EventType == RoutingService.ReprintEvent),
                // De ellas, las que se hicieron sin saber si la anterior había salido (desde «sin
                // confirmar»): pueden ser una copia de más (la marca «Posible copia» de la cola).
                ReprintsUnconfirmed = _dbContext.PrintJobEvents.Count(e => e.JobId == job.JobId
                    && e.EventType == RoutingService.ReprintEvent && e.OldStatus == PrintJobStatus.PrintedUnknown),
                // Desvío a la reserva (ERP_SERCA docs/75): de cuál vino y la reserva de la suya (el
                // botón; si se puede de verdad lo dice GET divert).
                job.DivertedFromPrinterId,
                DivertedFromName = _dbContext.Printers
                    .Where(p => p.PrinterId == job.DivertedFromPrinterId)
                    .Select(p => p.PrinterName)
                    .FirstOrDefault(),
                // El último desvío: qué pasó, cuándo y quién (la píldora «reserva» de la cola).
                DivertedMessage = job.DivertedFromPrinterId == null ? null : _dbContext.PrintJobEvents
                    .Where(e => e.JobId == job.JobId && e.EventType == DesvioService.DivertedEvent)
                    .OrderByDescending(e => e.EventId)
                    .Select(e => e.Message)
                    .FirstOrDefault(),
                DivertedAtUtc = job.DivertedFromPrinterId == null ? null : _dbContext.PrintJobEvents
                    .Where(e => e.JobId == job.JobId && e.EventType == DesvioService.DivertedEvent)
                    .OrderByDescending(e => e.EventId)
                    .Select(e => (DateTimeOffset?)e.OccurredAtUtc)
                    .FirstOrDefault(),
                DivertedBy = !verQuien || job.DivertedFromPrinterId == null ? null : _dbContext.PrintJobEvents
                    .Where(e => e.JobId == job.JobId && e.EventType == DesvioService.DivertedEvent)
                    .OrderByDescending(e => e.EventId)
                    .Select(e => e.ActorId)
                    .FirstOrDefault(),
                BackupPrinterId = printer == null ? null : printer.BackupPrinterId,
                BackupPrinterName = printer == null
                    ? null
                    : _dbContext.Printers
                        .Where(p => p.PrinterId == printer.BackupPrinterId)
                        .Select(p => p.PrinterName)
                        .FirstOrDefault()
            };

        if (includeTotal == true)
        {
            var total = await query.CountAsync(cancellationToken);
            var pagedResults = await projectedQuery
                .Skip((safePage - 1) * safeLimit)
                .Take(safeLimit)
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                Value = pagedResults,
                Count = total,
                Page = safePage,
                PageSize = safeLimit
            });
        }

        var results = (await projectedQuery
            .Take(safeLimit)
            .ToListAsync(cancellationToken))
            .Cast<object>()
            .ToList();

        return Ok(results);
    }

    // Las marcas de la cola: desviado a la reserva, reimpreso desde «sin confirmar» (posible copia), reimpreso
    // algo que sí había salido y reenviado a mano tras un error.
    private static readonly string[] Marcas = ["reserva", "copia", "reimpreso", "reintento"];

    /// <summary>
    /// Detalle de un trabajo (ERP_SERCA, 11/11/2026): sus datos y su historia entera, evento a evento. Quién
    /// hizo cada cosa, y el motivo y la nota de las reimpresiones, solo para quien puede reimprimir (como el
    /// registro de reimpresiones al que sustituye); los demás ven qué pasó y cuándo.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken cancellationToken)
    {
        if (await ForbidOutsideStoreAsync(id, cancellationToken) is { } denied)
            return denied;
        var job = await _dbContext.PrintJobs.AsNoTracking()
            .Where(j => j.JobId == id)
            .Select(j => new
            {
                j.JobId, j.ExternalJobId, j.StoreId, j.DocumentType, j.Channel, j.Status, j.PrinterId,
                j.DivertedFromPrinterId, j.IppJobId, j.AttemptCount, j.NextRetryAtUtc, j.LastErrorCode,
                j.LastErrorMessage, j.CreatedAtUtc, j.UpdatedAtUtc, j.LastRequeuedAtUtc,
                HasPdf = j.PdfBlob != null
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null)
            return NotFound();
        var eventos = await _dbContext.PrintJobEvents.AsNoTracking()
            .Where(e => e.JobId == id)
            .OrderBy(e => e.EventId)
            .ToListAsync(cancellationToken);
        // La reserva de su impresora: la ficha ofrece «A reserva» como la cola.
        var reservaId = job.PrinterId is null
            ? null
            : await _dbContext.Printers.AsNoTracking()
                .Where(p => p.PrinterId == job.PrinterId)
                .Select(p => p.BackupPrinterId)
                .FirstOrDefaultAsync(cancellationToken);
        var nombres = await _dbContext.Printers.AsNoTracking()
            .Where(p => p.PrinterId == job.PrinterId || p.PrinterId == job.DivertedFromPrinterId || p.PrinterId == reservaId)
            .ToDictionaryAsync(p => p.PrinterId, p => p.PrinterName, cancellationToken);
        var verQuien = User.VeQuien();

        return Ok(new
        {
            job.JobId, job.ExternalJobId, job.StoreId, job.DocumentType, job.Channel, job.Status,
            job.PrinterId,
            PrinterName = job.PrinterId is int p && nombres.TryGetValue(p, out var n) ? n : null,
            job.DivertedFromPrinterId,
            DivertedFromName = job.DivertedFromPrinterId is int d && nombres.TryGetValue(d, out var dn) ? dn : null,
            BackupPrinterId = reservaId,
            BackupPrinterName = reservaId is int r && nombres.TryGetValue(r, out var rn) ? rn : null,
            job.IppJobId, job.AttemptCount, job.NextRetryAtUtc, job.LastErrorCode, job.LastErrorMessage,
            LastErrorText = ErroresTrabajo.Texto(job.LastErrorCode),
            LastErrorHint = ErroresTrabajo.QueHacer(job.LastErrorCode),
            job.CreatedAtUtc, job.UpdatedAtUtc, job.LastRequeuedAtUtc, job.HasPdf,
            Events = eventos.Select(e =>
            {
                var reimpresion = e.EventType == RoutingService.ReprintEvent;
                var (motivo, nota, _) = reimpresion ? ReadReprintMetadata(e.MetadataJson) : (null, null, null);
                return new
                {
                    e.OccurredAtUtc,
                    e.EventType,
                    e.OldStatus,
                    e.NewStatus,
                    e.ErrorCode,
                    ErrorText = ErroresTrabajo.Texto(e.ErrorCode),
                    // La reimpresión lleva el motivo y la nota en el texto: sin permiso, solo que la hubo.
                    Message = reimpresion && !verQuien ? "Reimpresión de algo que ya había salido." : e.Message,
                    e.ActorType,
                    ActorId = verQuien || e.ActorType == "system" ? e.ActorId : null,
                    Reason = verQuien && motivo is not null
                        ? RoutingService.ReprintReasons.GetValueOrDefault(motivo, motivo)
                        : null,
                    Note = verQuien ? nota : null
                };
            }).ToList()
        });
    }

    /// <summary>
    /// Reintenta algo que no ha salido (Pending o ErrorFinal). Lo que ya salió se reimprime con
    /// motivo (POST reprint). Si no hay regla aplicable, transiciona a ErrorFinal con ROUTE_NOT_FOUND.
    /// </summary>
    [HttpPost("{id:guid}/route")]
    [Authorize(Policy = "StoreManagerOrAdmin")]
    public async Task<IActionResult> Route(Guid id, CancellationToken cancellationToken)
    {
        if (await ForbidOutsideStoreAsync(id, cancellationToken) is { } denied)
            return denied;
        return await ResendAsync(() => _routingService.TryRetryRouteAsync(id, cancellationToken, CurrentActor()));
    }

    /// <summary>
    /// Reimprime algo que ya salió, con o sin confirmación (mal impreso, perdido, mojado...). Motivo
    /// obligatorio; queda como evento REPRINT con quién la pidió (GET reprints).
    /// </summary>
    [HttpPost("{id:guid}/reprint")]
    [Authorize(Policy = "StoreManagerOrAdmin")]
    public async Task<IActionResult> Reprint(Guid id, [FromBody] ReprintRequest request, CancellationToken cancellationToken)
    {
        if (await ForbidOutsideStoreAsync(id, cancellationToken) is { } denied)
            return denied;
        // La copia anterior se lee antes: reimprimir borra su número de trabajo en la impresora.
        var anterior = await _dbContext.PrintJobs.AsNoTracking()
            .Where(j => j.JobId == id)
            .Select(j => new { j.Status, j.PrinterId, j.IppJobId, j.DocumentType, j.ExternalJobId })
            .FirstOrDefaultAsync(cancellationToken);
        return await ResendAsync(
            () => _routingService.ReprintAsync(id, request.Reason, request.Note, CurrentActor(), cancellationToken),
            // Si la anterior no se confirmó y sigue en la cola de la impresora, se cancela allí
            // (decisión del usuario, 27/10/2026): si no, al recuperarse salen las dos. Solo cuando
            // la nueva ya va a una impresora: si no, el documento se quedaría sin salir.
            anterior is { Status: PrintJobStatus.PrintedUnknown }
                ? () => CancelarCopiaAnteriorAsync(id, anterior.PrinterId, anterior.IppJobId,
                    anterior.DocumentType, anterior.ExternalJobId)
                : null);
    }

    // Sin token de la petición: la reimpresión ya está hecha en la BD y, si el navegador corta aquí, la
    // copia anterior se quedaría en la impresora sin constancia (como M1 en el Worker). IPP ya tiene
    // su propio plazo (IppTimeoutMs).
    private async Task<(string? Codigo, string? Aviso)> CancelarCopiaAnteriorAsync(
        Guid id, int? printerId, int? ippJobId, string documentType, string externalJobId)
    {
        var ct = CancellationToken.None;
        var resultado = await CancelarEnImpresoraAsync(printerId, ippJobId, documentType, externalJobId, ct);
        var (codigo, aviso) = resultado switch
        {
            null => ("SinNumero", "La copia anterior no tiene número en la impresora (salió por la cola de Windows o la impresora no lo dio): si sigue en su cola, cancélala allí o saldrán dos."),
            CancelacionEnImpresora.Cancelado => ("Cancelado", "La copia anterior seguía en la cola de la impresora y se ha cancelado: solo saldrá la nueva."),
            // La impresora dice que la anterior sí salió: es lo que faltaba saber de un «sin confirmar».
            CancelacionEnImpresora.YaSalio => ("YaSalio", "Según la impresora, la copia anterior sí había salido: con esta reimpresión habrá dos."),
            // Ya no está: nada que hacer, sale solo la nueva.
            CancelacionEnImpresora.YaNoSaldra or CancelacionEnImpresora.NoLoTiene =>
                (resultado.ToString(), (string?)null),
            _ => (resultado.ToString(), $"La copia anterior puede seguir en la impresora y no se ha cancelado ({Motivo(resultado.Value)}): si sale, habrá dos."),
        };
        if (aviso is not null)
        {
            // Constancia en el historial del trabajo, con quién reimprimió.
            _dbContext.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = id,
                EventType = "PREVIOUS_COPY_IN_PRINTER",
                ActorType = "user",
                ActorId = CurrentActor(),
                Message = aviso,
                OccurredAtUtc = _timeProvider.GetUtcNow()
            });
            await _dbContext.SaveChangesAsync(ct);
        }
        return (codigo, aviso);
    }

    private static string Motivo(CancelacionEnImpresora r) => r switch
    {
        CancelacionEnImpresora.NoEsNuestro => "ese número es ahora otro documento",
        CancelacionEnImpresora.SinNombre => "la impresora no dice el nombre del trabajo",
        CancelacionEnImpresora.NoDeja => "la impresora no deja cancelarla por la red",
        _ => "la impresora no responde",
    };

    /// <summary>
    /// Registro de reimpresiones (eventos REPRINT), lo más reciente primero: para que administración
    /// y los jefes de tienda comprueben quién reimprimió qué y por qué. Cada uno ve su tienda.
    /// Con <paramref name="window"/> («today», «7d», «30d») el periodo es el del dashboard y, como su
    /// KPI, solo cuenta tiendas activas: abierto desde el KPI, da las mismas (auditoría L8).
    /// </summary>
    [HttpGet("reprints")]
    [Authorize(Policy = "StoreManagerOrAdmin")]
    public async Task<IActionResult> GetReprints(
        [FromQuery] int? storeId,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int? limit,
        [FromQuery] string? window,
        [FromQuery] int? page,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
    {
        // Es quién reimprimió y por qué: el perfil compartido de tienda no lo ve (VeQuien).
        if (!User.VeQuien())
            return Forbid();
        if (reason is not null && !RoutingService.ReprintReasons.ContainsKey(reason))
            return BadRequest(new { error = "Motivo de reimpresión no válido." });
        var effectiveStoreId = User.VeTodasLasTiendas() ? storeId : GetCurrentUserStoreId();
        if (!User.VeTodasLasTiendas() && !effectiveStoreId.HasValue)
            return Forbid();
        if (window is not null)
            fromUtc = BusinessTimeZoneClock.WindowStartUtc(window, _timeProvider.GetUtcNow(), _businessTimeZone);

        var query =
            from e in _dbContext.PrintJobEvents.AsNoTracking()
            where e.EventType == RoutingService.ReprintEvent
            join j in _dbContext.PrintJobs.AsNoTracking() on e.JobId equals j.JobId
            select new { e, j.ExternalJobId, j.DocumentType, j.StoreId };
        // Con fechas sueltas, todas (también las de tiendas dadas de baja: es una auditoría).
        if (window is not null)
        {
            var activeStores = _dbContext.Stores.Where(s => s.IsActive);
            query = query.Where(x => activeStores.Any(s => s.StoreId == x.StoreId));
        }
        if (effectiveStoreId.HasValue)
            query = query.Where(x => x.StoreId == effectiveStoreId.Value);
        if (fromUtc.HasValue)
            query = query.Where(x => x.e.OccurredAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            query = query.Where(x => x.e.OccurredAtUtc <= toUtc.Value);

        // Cuántas hay de cada motivo en el periodo, antes de filtrar por motivo (resumen de la
        // pantalla). El motivo va en metadata_json.
        // ponytail: se leen los metadatos de todas las del periodo; son pocas (unas decenas al día).
        // Si crecen, agruparlas en SQL con metadata_json::jsonb->>'reason' (solo PostgreSQL).
        Dictionary<string, int>? byReason = null;
        if (page is not null)
            byReason = (await query.Select(x => x.e.MetadataJson).ToListAsync(cancellationToken))
                .GroupBy(m => ReadReprintMetadata(m).Reason ?? "")
                .ToDictionary(g => g.Key, g => g.Count());
        // Por su texto exacto, el que escribe RoutingService ({"reason":"PERDIDO",...}); el código ya
        // está validado contra ReprintReasons, sin comodines.
        if (reason is not null)
        {
            var needle = $"\"reason\":\"{reason}\"";
            query = query.Where(x => x.e.MetadataJson != null && x.e.MetadataJson.Contains(needle));
        }

        // Con page, paginado como la cola (WEB-2: 25, 50 o 100 por página); sin él, como antes (hasta 500).
        var size = page is null ? Math.Clamp(limit ?? 200, 1, 500) : Math.Clamp(limit ?? 25, 1, 100);
        var current = Math.Max(1, page ?? 1);
        var rows = await query
            .OrderByDescending(x => x.e.EventId)
            .Skip((current - 1) * size)
            .Take(size)
            .Select(x => new
            {
                x.e.EventId, x.e.JobId, x.e.OccurredAtUtc, x.e.ActorId, x.e.OldStatus, x.e.MetadataJson,
                x.ExternalJobId, x.DocumentType, x.StoreId
            })
            .ToListAsync(cancellationToken);

        // Motivo, nota e impresora del envío anterior van en metadata_json (lo escribe RoutingService).
        var detail = rows.ToDictionary(r => r.EventId, r => ReadReprintMetadata(r.MetadataJson));
        var printerIds = detail.Values.Select(d => d.PrinterId).OfType<int>().Distinct().ToList();
        var printers = await _dbContext.Printers.AsNoTracking()
            .Where(p => printerIds.Contains(p.PrinterId))
            .ToDictionaryAsync(p => p.PrinterId, p => p.PrinterName, cancellationToken);

        var value = rows.Select(r =>
        {
            var d = detail[r.EventId];
            return new
            {
                r.EventId, r.JobId, r.ExternalJobId, r.DocumentType, r.StoreId, r.OccurredAtUtc,
                Actor = r.ActorId,
                PreviousStatus = r.OldStatus,
                d.Reason,
                ReasonText = d.Reason is not null && RoutingService.ReprintReasons.TryGetValue(d.Reason, out var t) ? t : d.Reason,
                d.Note,
                PrinterName = d.PrinterId is { } pid && printers.TryGetValue(pid, out var name) ? name : null
            };
        });
        if (page is null)
            return Ok(value);
        return Ok(new
        {
            value,
            count = await query.CountAsync(cancellationToken),
            page = current,
            pageSize = size,
            byReason
        });
    }

    private static (string? Reason, string? Note, int? PrinterId) ReadReprintMetadata(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return (null, null, null);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string? Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int? printerId = root.TryGetProperty("printerId", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
        return (Text("reason"), Text("note"), printerId);
    }

    private async Task<IActionResult> ResendAsync(
        Func<Task<RouteResult>> resend,
        // Lo que se hace cuando el reenvío ya va a una impresora (reimprimir: la copia anterior).
        Func<Task<(string? Codigo, string? Aviso)>>? trasEnrutar = null)
    {
        try
        {
            var result = await resend();
            if (!result.Success)
                return Ok(new { status = "ErrorFinal", errorCode = result.ErrorCode });
            var (codigo, aviso) = trasEnrutar is null ? (null, null) : await trasEnrutar();
            return Ok(new { status = "Routed", printerId = result.PrinterId, printerCancel = codigo, printerMessage = aviso });
        }
        catch (PrintJobStateConflictException ex)
        {
            // El Worker lo reclamo mientras se preparaba el reenvío. No es una peticion mal
            // formada: releyendo el estado puede volver a intentarse, y por eso es 409 y no 400.
            return Conflict(new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Si el trabajo se puede mandar a la reserva de su impresora y, si no, por qué (ERP_SERCA docs/75,
    /// tabla de §5). No cambia nada: lo pregunta el botón antes de desviar.
    /// </summary>
    [HttpGet("{id:guid}/divert")]
    public async Task<IActionResult> DivertCheck(Guid id, [FromServices] DesvioService desvio, CancellationToken cancellationToken)
    {
        if (await ForbidOutsideStoreAsync(id, cancellationToken) is { } denied)
            return denied;
        try
        {
            var e = await desvio.EvaluarAsync(id, cancellationToken);
            return Ok(new
            {
                e.Posible, e.Motivo, e.ReservaId, e.ReservaNombre, e.YaEnviado,
                e.OriginalNombre, e.OriginalEstado, e.OriginalDesde, e.ReservaEstado
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Manda el trabajo a la impresora de reserva (docs/75, F2). Si ya estaba en la original, primero se
    /// cancela allí; si no se puede confirmar que no saldrá, responde «Incierta» y no desvía, salvo que se
    /// acepte el duplicado. 409 solo si el estado cambió entre medias.
    /// </summary>
    [HttpPost("{id:guid}/divert")]
    [Authorize(Policy = "StoreManagerOrAdmin")]
    public async Task<IActionResult> Divert(Guid id, [FromBody] DivertRequest? request,
        [FromServices] DesvioService desvio, CancellationToken cancellationToken)
    {
        if (await ForbidOutsideStoreAsync(id, cancellationToken) is { } denied)
            return denied;
        try
        {
            // Sin el token: si el navegador corta tras cancelar en la original, el trabajo tiene que
            // quedar desviado (y con su evento), no cancelado allí y sin ir a ninguna parte.
            // «Incierta» no es un error: pide aceptar el duplicado y volver a pedirlo.
            var r = await desvio.DesviarAsync(id, CurrentActor(), request?.AceptaDuplicado == true, CancellationToken.None);
            return Ok(new { codigo = r.Codigo, mensaje = r.Mensaje, printerId = r.ReservaId });
        }
        catch (PrintJobStateConflictException ex)
        {
            return Conflict(new { codigo = "Conflicto", error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Fuera de su tienda, ni reintentar ni reimprimir (quien ve todas, sí).</summary>
    private async Task<IActionResult?> ForbidOutsideStoreAsync(Guid id, CancellationToken cancellationToken)
    {
        if (User.VeTodasLasTiendas())
            return null;
        var userStoreId = GetCurrentUserStoreId();
        if (!userStoreId.HasValue)
            return Forbid();

        var jobStoreId = await _dbContext.PrintJobs
            .AsNoTracking()
            .Where(j => j.JobId == id)
            .Select(j => (int?)j.StoreId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!jobStoreId.HasValue)
            return NotFound();
        return jobStoreId.Value != userStoreId.Value ? Forbid() : null;
    }

    /// <summary>Quién hace la acción, para actor_id: con el pase del ERP, «ERP código nombre».</summary>
    private string? CurrentActor()
        => User.Identity?.Name ?? User.FindFirstValue("Login") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Cancela manualmente un trabajo para retirarlo del flujo activo.
    /// Permitido para StoreManager/Admin en su ambito de tienda.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "StoreManagerOrAdmin")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        // Proyeccion sin seguimiento: no hace falta cargar el PdfBlob para cancelar, y la
        // escritura va por UPDATE condicional, no por el ChangeTracker.
        var job = await _dbContext.PrintJobs
            .AsNoTracking()
            .Where(j => j.JobId == id)
            .Select(j => new { j.StoreId, j.Status, j.PrinterId, j.IppJobId, j.DocumentType, j.ExternalJobId })
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
            return NotFound();

        if (!User.VeTodasLasTiendas())
        {
            var userStoreId = GetCurrentUserStoreId();
            if (!userStoreId.HasValue || job.StoreId != userStoreId.Value)
                return Forbid();
        }

        var cancellableStates = new[]
        {
            PrintJobStatus.Pending,
            PrintJobStatus.Routed,
            PrintJobStatus.RetryScheduled,
            PrintJobStatus.ErrorFinal,
            // Estados de incertidumbre: el watchdog los produce de forma rutinaria (una impresora
            // sin IPP manda ahí todos sus trabajos) y sin esto quedaban sin ninguna acción posible
            // desde la interfaz — la única salida era un UPDATE manual en la BD.
            PrintJobStatus.PrintedUnknown,
            PrintJobStatus.PrinterBlocked
        };

        if (!cancellableStates.Contains(job.Status))
        {
            return BadRequest(new
            {
                error = $"El job {id} no puede cancelarse en estado {job.Status}."
            });
        }

        var now = _timeProvider.GetUtcNow();
        var oldStatus = job.Status;

        // Fase 2.5: el estado leido va en el WHERE. Si el Worker reclamo el trabajo entre la
        // lectura y esta escritura, esto afecta a 0 filas y respondemos conflicto — antes se
        // devolvia "Cancelled" mientras el papel salia igual por la impresora.
        var cancelled = await _dbContext.PrintJobs
            .Where(j => j.JobId == id && j.Status == oldStatus)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, PrintJobStatus.Cancelled)
                .SetProperty(x => x.NextRetryAtUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);

        if (cancelled != 1)
        {
            return Conflict(new
            {
                error = $"El job {id} cambio de estado mientras se cancelaba; vuelva a consultarlo antes de reintentar."
            });
        }

        // También en la cola de la impresora (decisión del usuario, 27/10/2026): si no, sale cuando
        // se recupera y duplica en silencio. Ya con el UPDATE hecho y sin transacción abierta: si no se
        // puede, el trabajo sigue cancelado aquí y el motivo queda en el evento y en la respuesta.
        var (enImpresora, aviso) = TextoAlCancelar(
            await CancelarEnImpresoraAsync(job.PrinterId, job.IppJobId, job.DocumentType, job.ExternalJobId, CancellationToken.None),
            oldStatus);

        _dbContext.PrintJobEvents.Add(new PrintJobEvent
        {
            JobId = id,
            EventType = "CANCELLED_BY_USER",
            OldStatus = oldStatus,
            NewStatus = PrintJobStatus.Cancelled,
            ActorType = "user",
            ActorId = CurrentActor(),
            Message = "Trabajo cancelado manualmente desde interfaz operativa." + (aviso is null ? "" : $" {aviso}"),
            OccurredAtUtc = now
        });

        // Ya cancelado en la BD: la constancia se guarda aunque el navegador corte la petición.
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return Ok(new { status = "Cancelled", printerCancel = enImpresora, printerMessage = aviso });
    }

    /// <summary>
    /// Cancela en la impresora lo que se mandó por IP, con su número y comprobando que sigue siendo el
    /// nuestro. Null si no tiene número de trabajo (salió por la cola de Windows o no había salido).
    /// </summary>
    private async Task<CancelacionEnImpresora?> CancelarEnImpresoraAsync(
        int? printerId, int? ippJobId, string documentType, string externalJobId, CancellationToken ct)
    {
        if (ippJobId is null)
            return null;
        var host = printerId is null
            ? null
            : await _dbContext.Printers.AsNoTracking()
                .Where(p => p.PrinterId == printerId)
                .Select(p => p.Host)
                .FirstOrDefaultAsync(ct);
        return _ipp is null || string.IsNullOrWhiteSpace(host)
            ? CancelacionEnImpresora.SinRespuesta
            : await _ipp.CancelJobAsync(host, ippJobId.Value, IIppConfirmationService.JobNameFor(documentType, externalJobId), ct);
    }

    // Qué decir al cancelar a mano, para el evento y para quien lo pidió. Sin número: si pudo salir
    // (sin confirmar, bloqueado), se avisa de mirarla; si no había salido, nada.
    private static (string? Codigo, string? Aviso) TextoAlCancelar(CancelacionEnImpresora? resultado, PrintJobStatus estado) =>
        resultado switch
        {
            null => estado is PrintJobStatus.PrintedUnknown or PrintJobStatus.PrinterBlocked
                ? ("SinNumero", "No tiene número de trabajo en la impresora (salió por la cola de Windows o la impresora no lo dio): si sigue en su cola, cancélalo allí o saldrá.")
                : (null, null),
            CancelacionEnImpresora.Cancelado => ("Cancelado", "También se canceló en la impresora: no saldrá."),
            CancelacionEnImpresora.YaSalio => ("YaSalio", "La impresora ya lo había impreso: ha salido."),
            CancelacionEnImpresora.YaNoSaldra => ("YaNoSaldra", "En la impresora ya estaba cancelado: no saldrá."),
            CancelacionEnImpresora.NoLoTiene => ("NoLoTiene", "La impresora ya no lo tiene en su cola: no saldrá (si salió, fue antes)."),
            CancelacionEnImpresora.NoEsNuestro => ("NoEsNuestro", "Ese número de trabajo es ahora otro documento en la impresora y no se ha tocado: si el nuestro sigue en ella, cancélalo en su panel."),
            CancelacionEnImpresora.SinNombre => ("SinNombre", "La impresora no dice el nombre del trabajo y no se puede comprobar que sea el nuestro: si sigue en su cola, cancélalo en su panel."),
            CancelacionEnImpresora.NoDeja => ("NoDeja", "La impresora no deja cancelarlo por la red: cancélalo en su panel o saldrá al recuperarse."),
            _ => ("SinRespuesta", "La impresora no responde: si sigue en su cola, cancélalo en su panel o saldrá al recuperarse."),
        };

    private int? GetCurrentUserStoreId()
    {
        var claimValue = User.FindFirstValue("StoreId");
        return int.TryParse(claimValue, out var storeId) ? storeId : null;
    }
}

/// <summary>Desvío a la reserva (docs/75): AceptaDuplicado, si no se pudo confirmar que la original no lo imprimirá.</summary>
public record DivertRequest(bool AceptaDuplicado = false);

/// <summary>Motivo: una clave de RoutingService.ReprintReasons; con «OTRO», la nota es obligatoria.</summary>
public record ReprintRequest(string? Reason, string? Note);
