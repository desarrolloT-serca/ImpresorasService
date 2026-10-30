using System.Security.Claims;
using ImpresorasService.Api.Security;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Connectivity;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Authorize(Policy = "EmployeeOrAbove")]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly ImpresorasDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _businessTimeZone;
    private readonly ILogger<DashboardController> _logger;
    private readonly IDashboardThresholdRuleStore _ruleStore;
    private const int DefaultWarningQueueMin = 10;
    private const int DefaultCriticalQueueMin = 30;
    private const int DefaultWarningFailedWithoutRetryMin = 1;
    private const int DefaultCriticalFailedWithoutRetryMin = 5;
    private const int DefaultMissingHostMin = 1;
    private const int DefaultConnWarningFailuresMin = 2;
    private const int DefaultConnCriticalFailuresMin = 3;

    public DashboardController(
        ImpresorasDbContext dbContext, TimeProvider timeProvider, IConfiguration configuration,
        ILogger<DashboardController> logger, IDashboardThresholdRuleStore ruleStore)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
        _ruleStore = ruleStore;
        // Compartido con StoreHealthAlertBackgroundService (Worker) — KPI-P2-004, mismo reloj de negocio.
        _businessTimeZone = BusinessTimeZoneClock.Resolve(configuration["Dashboard:BusinessTimeZone"], _logger);
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(
        [FromQuery] string? window,
        [FromQuery] int? storeId,
        CancellationToken cancellationToken)
    {
        var effectiveStoreId = User.VeTodasLasTiendas() ? storeId : GetCurrentUserStoreId();
        // Sin tienda no se ve ninguna, nunca todas: igual que la cola y las impresoras.
        if (!User.VeTodasLasTiendas() && !effectiveStoreId.HasValue)
            return Forbid();
        var thresholds = await GetThresholdsAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var fromUtc = ResolveWindowStartUtc(window, now);

        var stores = _dbContext.Stores.AsNoTracking().Where(x => x.IsActive);
        var printers = _dbContext.Printers.AsNoTracking().Where(x => x.IsActive);
        // El dashboard operativo solo muestra tiendas activas; excluye histórico de tiendas dadas de baja.
        // Consulta propia (no `stores`, que se acota más abajo por tienda) para que el EXISTS no cambie.
        var activeStores = _dbContext.Stores.Where(s => s.IsActive);
        var jobs = _dbContext.PrintJobs.AsNoTracking()
            .Where(x => activeStores.Any(s => s.StoreId == x.StoreId));

        if (effectiveStoreId.HasValue)
        {
            jobs = jobs.Where(x => x.StoreId == effectiveStoreId.Value);
            stores = stores.Where(x => x.StoreId == effectiveStoreId.Value);
            printers = printers.Where(x => x.StoreId == effectiveStoreId.Value);
        }

        // received: cohorte de creados en la ventana (foto de qué entró). failedWithoutRetryCurrent:
        // foto actual de fallos sin reenvío — SIN ventana (A-KPI-01):
        // un ErrorFinal es terminal, su UpdatedAtUtc no vuelve a moverse, así que filtrarlo por ventana
        // lo hacía "desaparecer" al cruzar medianoche aunque el job siguiera roto. printed/failed:
        // eventos de negocio (PrintJobEvents), contados una sola vez por job — KPI-P1-001/002,
        //. Acotada también por arriba (<= now), igual que PHP
        // (resolveWindowRange) — VAL-P3-009.
        var jobsInWindow = jobs.Where(x => x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc <= now);

        var (printedRows, failedRows) = await LoadPrintedAndFailedAsync(jobs, fromUtc, now, cancellationToken);

        // BuildStoreRowsAsync ya agrega received/queue/failed por tienda en la BD;
        // sumar en memoria evita 5 COUNT queries adicionales (AUD-18).
        var rules = await _ruleStore.LoadAsync(cancellationToken);
        var (storeRows, alerts) = await BuildStoreRowsAsync(stores, printers, jobsInWindow, jobs, printedRows, failedRows, thresholds, rules, cancellationToken);

        // Reimpresiones de lo que ya había salido (posibles duplicados) pedidas en el periodo: el
        // control de que no se disparen. El detalle, en GET /api/printjobs/reprints.
        var reprints = await _dbContext.PrintJobEvents
            .Where(e => e.EventType == RoutingService.ReprintEvent && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc <= now)
            .Join(jobs, e => e.JobId, j => j.JobId, (e, j) => e.EventId)
            .CountAsync(cancellationToken);

        var kpis = new
        {
            received = storeRows.Sum(r => r.Received),
            printed = printedRows.Count,
            failed = failedRows.Count,
            reprints,
            queueCurrent = storeRows.Sum(r => r.QueuedCurrent),
            failedWithoutRetryCurrent = storeRows.Sum(r => r.FailedWithoutRetryCurrent),
            activePrinters = storeRows.Sum(r => r.ConnectedPrinters),
            activeStores = storeRows.Count
        };

        // Observabilidad de negocio (A-ARCH-05, G4.3): sin backend de métricas (OTel/Prometheus) en
        // el proyecto, se usa logging estructurado — mismo patrón que IngestionService/StoreHealthAlertBackgroundService.
        _logger.LogInformation(
            "Dashboard overview KPIs. window={Window} storeId={StoreId} received={Received} printed={Printed} failed={Failed} queueCurrent={QueueCurrent} failedWithoutRetryCurrent={FailedWithoutRetryCurrent} activePrinters={ActivePrinters} activeStores={ActiveStores}",
            NormalizeWindow(window), effectiveStoreId, kpis.received, kpis.printed, kpis.failed,
            kpis.queueCurrent, kpis.failedWithoutRetryCurrent, kpis.activePrinters, kpis.activeStores);

        return Ok(new
        {
            generatedAtUtc = now,
            appliedFilters = new
            {
                window = NormalizeWindow(window),
                storeId = effectiveStoreId
            },
            kpis,
            thresholds,
            alerts,
            stores = storeRows
        });
    }

    /// <summary>
    /// Serie temporal del periodo para el dashboard: llegadas, impresos y fallos por tramo, mas la
    /// profundidad de cola reconstruida: `queueDepth` al final de cada tramo y `queuePeak`, la mayor
    /// que hubo dentro. Mismas definiciones que `overview` (cohorte por
    /// CreatedAtUtc, PrintJobEvents para impreso/fallo), para que la grafica y los KPIs no puedan
    /// contar cosas distintas. `outcome` reparte lo que llego en cada tramo por su estado de ahora.
    ///
    /// La cola no se puede consultar en pasado —`queueCurrent` es una foto— asi que se reconstruye
    /// caminando hacia atras desde la foto de ahora: depth[n-1] = depth[n] - entradas[n] + salidas[n]
    /// (entradas: llegadas mas vueltas a la cola por reintento o reimpresion).
    /// Sale exacta y ahorra la consulta de sembrado que haria falta yendo hacia delante.
    ///
    /// ponytail: los tramos se agrupan en memoria (se traen los instantes, no filas completas). Es la
    /// misma lógica en PostgreSQL y en los tests sobre SQLite, que no sabe truncar fechas. Techo: una
    /// ventana de 30 días con cientos de miles de trabajos; si el dashboard se nota lento, agrupar en
    /// SQL con date_trunc (solo PostgreSQL) y cubrirlo con una prueba contra PostgreSQL real.
    /// </summary>
    [HttpGet("timeseries")]
    public async Task<IActionResult> GetTimeseries(
        [FromQuery] string? window,
        [FromQuery] int? storeId,
        [FromQuery] int? bucketMinutes,
        CancellationToken cancellationToken)
    {
        var effectiveStoreId = User.VeTodasLasTiendas() ? storeId : GetCurrentUserStoreId();
        if (!User.VeTodasLasTiendas() && !effectiveStoreId.HasValue)
            return Forbid();
        var now = _timeProvider.GetUtcNow();
        var fromUtc = ResolveWindowStartUtc(window, now);
        var normalized = NormalizeWindow(window);
        // Sin bucketMinutes, como siempre (lo usa el PHP): por horas hoy y por días en 7 y 30 días.
        // Con él, la franja que se pida (0 = automática) y alineada a la hora de negocio.
        var bucket = normalized == "today" ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
        if (bucketMinutes is not null)
        {
            var minutos = ElegirFranja(bucketMinutes.Value, now - fromUtc);
            if (minutos is null)
                return BadRequest(new
                {
                    error = $"Franja no válida para el periodo: {string.Join(", ", Franjas)} minutos, " +
                            $"0 para la automática, y como mucho {MaxTramos} tramos."
                });
            bucket = TimeSpan.FromMinutes(minutos.Value);
        }

        var activeStores = _dbContext.Stores.Where(s => s.IsActive);
        var jobs = _dbContext.PrintJobs.AsNoTracking().Where(x => activeStores.Any(s => s.StoreId == x.StoreId));
        if (effectiveStoreId.HasValue)
            jobs = jobs.Where(x => x.StoreId == effectiveStoreId.Value);

        // Con su estado de ahora: el resultado de lo que llegó en cada tramo (outcome).
        var arrived = await jobs
            .Where(x => x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc <= now)
            .Select(x => new { x.CreatedAtUtc, x.Status })
            .ToListAsync(cancellationToken);
        var arrivals = arrived.Select(x => x.CreatedAtUtc).ToList();

        // Primer instante impreso por trabajo — KPI-P1-001, igual que `printed` del overview.
        var printedAt = await _dbContext.PrintJobEvents
            .Where(e => e.NewStatus != null && DashboardPrintJobPredicates.PrintedStatuses.Contains(e.NewStatus.Value))
            .Join(jobs, e => e.JobId, j => j.JobId, (e, j) => new { e.JobId, e.OccurredAtUtc })
            .GroupBy(x => x.JobId)
            .Select(g => g.Min(x => x.OccurredAtUtc))
            .Where(t => t >= fromUtc && t <= now)
            .ToListAsync(cancellationToken);

        // Primera senal de fallo por trabajo, para que un job que reintenta varias veces no pinte
        // un pico por cada intento.
        var failedAt = await _dbContext.PrintJobEvents
            .Where(e => e.NewStatus == PrintJobStatus.ErrorFinal || e.NewStatus == PrintJobStatus.RetryScheduled)
            .Join(jobs, e => e.JobId, j => j.JobId, (e, j) => new { e.JobId, e.OccurredAtUtc })
            .GroupBy(x => x.JobId)
            .Select(g => g.Min(x => x.OccurredAtUtc))
            .Where(t => t >= fromUtc && t <= now)
            .ToListAsync(cancellationToken);

        // Salidas de cola: cada paso de un estado de cola (o del alta, sin estado previo) a uno que ya
        // no lo es. RetryScheduled sigue dentro de QueueStatuses, asi que un reintento automatico no
        // cuenta como salida.
        var departures = await _dbContext.PrintJobEvents
            .Where(e => e.NewStatus != null && !DashboardPrintJobPredicates.QueueStatuses.Contains(e.NewStatus.Value)
                && (e.OldStatus == null || DashboardPrintJobPredicates.QueueStatuses.Contains(e.OldStatus.Value))
                && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc <= now)
            .Join(jobs, e => e.JobId, j => j.JobId, (e, j) => e.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        // Vueltas a la cola sin ser llegadas: reintento manual y reimpresion (de fuera de la cola a
        // Pending). Sin ellas, lo reimpreso que sigue en cola descuadraba la reconstruccion.
        var requeues = await _dbContext.PrintJobEvents
            .Where(e => e.OldStatus != null && !DashboardPrintJobPredicates.QueueStatuses.Contains(e.OldStatus.Value)
                && e.NewStatus != null && DashboardPrintJobPredicates.QueueStatuses.Contains(e.NewStatus.Value)
                && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc <= now)
            .Join(jobs, e => e.JobId, j => j.JobId, (e, j) => e.OccurredAtUtc)
            .ToListAsync(cancellationToken);
        var entries = arrivals.Concat(requeues).ToList();

        var queueNow = await jobs.CountAsync(
            j => DashboardPrintJobPredicates.QueueStatuses.Contains(j.Status), cancellationToken);

        var starts = new List<DateTimeOffset>();
        if (bucketMinutes is null)
            for (var t = FloorTo(fromUtc, bucket); t <= now; t = t + bucket)
                starts.Add(t);
        else
        {
            // Fronteras en hora de negocio, pasadas a UTC una a una: con cambio de hora los días siguen
            // empezando a las 00:00 y los tramos de 4 h, a las 00, 04, 08... (el 25/10/2026 el día salía
            // a las 23:00). La hora que se salta en marzo no tiene tramo.
            // ponytail: en la hora que se repite en octubre, sus dos pasadas caen en el mismo tramo.
            for (var local = FloorTo(TimeZoneInfo.ConvertTime(fromUtc, _businessTimeZone), bucket).DateTime; ; local += bucket)
            {
                if (_businessTimeZone.IsInvalidTime(local))
                    continue;
                var t = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _businessTimeZone));
                if (t > now)
                    break;
                starts.Add(t);
            }
        }
        if (starts.Count == 0)
            starts.Add(FloorTo(now, bucket));

        // Por búsqueda y no dividiendo: con cambio de hora los tramos no miden todos lo mismo.
        int[] Bucketize(List<DateTimeOffset> stamps)
        {
            var counts = new int[starts.Count];
            foreach (var stamp in stamps)
            {
                var index = starts.BinarySearch(stamp);
                if (index < 0)
                    index = ~index - 1;
                if (index >= 0)
                    counts[index]++;
            }
            return counts;
        }

        var received = Bucketize(arrivals);
        // Lo que llegó en cada tramo, por cómo está ahora: cada trabajo cuenta una vez y un fallo sin
        // resolver sigue a la vista hasta que se resuelve (al reimprimirlo y salir, pasa a impreso).
        int[] ArrivedAs(Func<PrintJobStatus, bool> estado) =>
            Bucketize(arrived.Where(x => estado(x.Status)).Select(x => x.CreatedAtUtc).ToList());
        var arrivedPrinted = ArrivedAs(s => DashboardPrintJobPredicates.PrintedStatuses.Contains(s));
        var arrivedFailed = ArrivedAs(s => s is PrintJobStatus.ErrorFinal or PrintJobStatus.PrinterBlocked);
        var arrivedCancelled = ArrivedAs(s => s == PrintJobStatus.Cancelled);
        var printed = Bucketize(printedAt);
        var failed = Bucketize(failedAt);
        var left = Bucketize(departures);
        var entered = Bucketize(entries);

        // Marcha atras desde la foto actual: el ultimo tramo cierra en queueNow.
        var depth = new int[starts.Count];
        var running = queueNow;
        for (var i = starts.Count - 1; i >= 0; i--)
        {
            depth[i] = running;
            running = Math.Max(0, running - entered[i] + left[i]);
        }

        // Maximo dentro de cada tramo: hacia delante, entrada a entrada y salida a salida, desde la cola
        // al empezar el periodo (lo que dejo la marcha atras). La cola de un trabajo dura segundos, asi
        // que la del final del tramo casi siempre es 0 aunque dentro hubiera varios esperando.
        var steps = entries.Select(t => (t, d: 1)).Concat(departures.Select(t => (t, d: -1)))
            .OrderBy(x => x.t).ThenBy(x => x.d) // a la misma hora, primero la salida: no infla el pico
            .ToList();
        var peak = new int[starts.Count];
        var level = running;
        var k = 0;
        for (var i = 0; i < starts.Count; i++)
        {
            peak[i] = level;
            var end = i + 1 < starts.Count ? starts[i + 1] : DateTimeOffset.MaxValue;
            for (; k < steps.Count && steps[k].t < end; k++)
            {
                level = Math.Max(0, level + steps[k].d);
                peak[i] = Math.Max(peak[i], level);
            }
            peak[i] = Math.Max(peak[i], depth[i]);
        }

        var points = starts
            .Select((start, i) => new
            {
                startUtc = start,
                received = received[i],
                printed = printed[i],
                failed = failed[i],
                queueDepth = depth[i],
                queuePeak = peak[i],
                outcome = new
                {
                    printed = arrivedPrinted[i],
                    failed = arrivedFailed[i],
                    cancelled = arrivedCancelled[i],
                    // En cola, enviándose o reintentando.
                    active = received[i] - arrivedPrinted[i] - arrivedFailed[i] - arrivedCancelled[i]
                }
            })
            .ToList();

        return Ok(new
        {
            generatedAtUtc = now,
            appliedFilters = new { window = normalized, storeId = effectiveStoreId },
            bucket = bucket < TimeSpan.FromDays(1) ? "hour" : "day",
            bucketMinutes = (int)bucket.TotalMinutes,
            points
        });
    }

    // Franjas de la gráfica, en minutos. La automática es la menor que deja el periodo en
    // MaxTramosAuto tramos o menos: hoy, de 15 min a 1 h según la hora; 7 días, 4 h; 30 días, un día.
    internal static readonly int[] Franjas = [15, 30, 60, 120, 240, 720, 1440];
    private const int MaxTramosAuto = 48;
    internal const int MaxTramos = 500;

    public static int? ElegirFranja(int pedida, TimeSpan periodo)
    {
        int Tramos(int minutos) => (int)Math.Ceiling(periodo.TotalMinutes / minutos);
        if (pedida == 0)
            return Franjas.FirstOrDefault(f => Tramos(f) <= MaxTramosAuto, Franjas[^1]);
        return Franjas.Contains(pedida) && Tramos(pedida) <= MaxTramos ? pedida : null;
    }

    private static DateTimeOffset FloorTo(DateTimeOffset value, TimeSpan bucket) =>
        new(value.Ticks - (value.Ticks % bucket.Ticks), value.Offset);

    [HttpGet("thresholds")]
    public async Task<IActionResult> GetThresholds(CancellationToken cancellationToken)
    {
        var thresholds = await GetThresholdsAsync(cancellationToken);
        return Ok(thresholds);
    }

    /// <summary>
    /// Reglas de severidad de 1-3 niveles por métrica (G5.3) — sustituye a dashboard-threshold-rules.json
    /// de PHP; la Api es ahora la fuente única de verdad, consumida también por el Worker.
    /// </summary>
    [HttpGet("threshold-rules")]
    public async Task<IActionResult> GetThresholdRules(CancellationToken cancellationToken)
    {
        var rules = await _ruleStore.LoadAsync(cancellationToken);
        return Ok(ToDto(rules));
    }

    [HttpPut("threshold-rules")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateThresholdRules([FromBody] ThresholdRuleSetRequest request, CancellationToken cancellationToken)
    {
        // Lo opcional que no venga (un cliente anterior, el PHP) se queda como está.
        var actual = await _ruleStore.LoadAsync(cancellationToken);
        var rules = new ThresholdRuleSet(
            ToRules(request.Queue), ToRules(request.Failed), ToRules(request.MissingHost), ToRules(request.Conn))
        {
            Silent = request.Silent is null ? actual.Silent : ToRules(request.Silent),
            Reprints = request.Reprints is null ? actual.Reprints : ToRules(request.Reprints),
            Horario = request.Horario is null
                ? actual.Horario
                : new HorarioTienda(request.Horario.Dias ?? [], request.Horario.Desde ?? "", request.Horario.Hasta ?? ""),
            // Lo edita la pantalla de Telegram (PUT /api/telegram/config): aquí se conserva.
            TelegramSoloEnHorario = actual.TelegramSoloEnHorario
        };

        var errors = await _ruleStore.SaveAsync(rules, cancellationToken);
        if (errors.Count > 0)
            return BadRequest(new { errors });

        return Ok(ToDto(await _ruleStore.LoadAsync(cancellationToken)));
    }

    private static List<ThresholdRule> ToRules(List<ThresholdRuleRequest>? rules) =>
        (rules ?? []).Select(r => new ThresholdRule(r.Min, r.Severity ?? "warning")).ToList();

    private static object ToDto(ThresholdRuleSet rules) => new
    {
        queue = rules.Queue.Select(r => new { r.Min, r.Severity }),
        failed = rules.Failed.Select(r => new { r.Min, r.Severity }),
        missingHost = rules.MissingHost.Select(r => new { r.Min, r.Severity }),
        conn = rules.Conn.Select(r => new { r.Min, r.Severity }),
        silent = rules.Silent.Select(r => new { r.Min, r.Severity }),
        reprints = rules.Reprints.Select(r => new { r.Min, r.Severity }),
        horario = new { dias = rules.Horario.Dias, desde = rules.Horario.Desde, hasta = rules.Horario.Hasta }
    };

    [HttpPut("thresholds")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateThresholds([FromBody] UpdateDashboardThresholdsRequest request, CancellationToken cancellationToken)
    {
        if (request.WarningQueueMin < 0 || request.CriticalQueueMin < 0
            || request.WarningFailedWithoutRetryMin < 0 || request.CriticalFailedWithoutRetryMin < 0
            || request.MissingHostMin < 0
            || request.ConnWarningFailuresMin < 0 || request.ConnCriticalFailuresMin < 0)
        {
            return BadRequest(new { error = "Los umbrales no pueden ser negativos." });
        }

        if (request.WarningQueueMin >= request.CriticalQueueMin)
        {
            return BadRequest(new { error = "Warning de cola debe ser menor que Critical de cola." });
        }

        if (request.WarningFailedWithoutRetryMin >= request.CriticalFailedWithoutRetryMin)
        {
            return BadRequest(new { error = "Warning de fallos debe ser menor que Critical de fallos." });
        }

        if (request.ConnWarningFailuresMin >= request.ConnCriticalFailuresMin)
        {
            return BadRequest(new { error = "Warning de conectividad debe ser menor que Critical de conectividad." });
        }

        var thresholdsRow = await _dbContext.DashboardThresholds
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (thresholdsRow is null)
        {
            thresholdsRow = new DashboardThreshold { Id = 1 };
            await _dbContext.DashboardThresholds.AddAsync(thresholdsRow, cancellationToken);
        }

        thresholdsRow.WarningQueueMin = request.WarningQueueMin;
        thresholdsRow.CriticalQueueMin = request.CriticalQueueMin;
        thresholdsRow.QueueWarningSeverity = NormalizeSeverity(request.QueueWarningSeverity);
        thresholdsRow.QueueCriticalSeverity = NormalizeSeverity(request.QueueCriticalSeverity);
        thresholdsRow.WarningFailedWithoutRetryMin = request.WarningFailedWithoutRetryMin;
        thresholdsRow.CriticalFailedWithoutRetryMin = request.CriticalFailedWithoutRetryMin;
        thresholdsRow.FailedWarningSeverity = NormalizeSeverity(request.FailedWarningSeverity);
        thresholdsRow.FailedCriticalSeverity = NormalizeSeverity(request.FailedCriticalSeverity);
        thresholdsRow.MissingHostMin = request.MissingHostMin;
        thresholdsRow.MissingHostSeverity = NormalizeSeverity(request.MissingHostSeverity);
        thresholdsRow.ConnWarningFailuresMin = request.ConnWarningFailuresMin;
        thresholdsRow.ConnCriticalFailuresMin = request.ConnCriticalFailuresMin;
        thresholdsRow.ConnWarningSeverity = NormalizeSeverity(request.ConnWarningSeverity);
        thresholdsRow.ConnCriticalSeverity = NormalizeSeverity(request.ConnCriticalSeverity);
        thresholdsRow.UpdatedAtUtc = _timeProvider.GetUtcNow();

        await _dbContext.SaveChangesAsync(cancellationToken);

        var updated = await GetThresholdsAsync(cancellationToken);
        return Ok(updated);
    }

    /// <summary>
    /// "printed": primera vez que cada JobId entra en un estado de <see cref="DashboardPrintJobPredicates.PrintedStatuses"/>
    /// dentro de la ventana (no cada actualización posterior sobre el mismo estado impreso —
    /// KPI-P1-001). "failed": JobId distintos con al menos una señal de fallo en la ventana
    /// (entrada a ErrorFinal/RetryScheduled, o primera impresión con AttemptCount>1 — KPI-P1-002).
    /// Un mismo job nunca suma más de una vez a cada KPI dentro de la misma ventana.
    ///
    /// Perf (A-KPI-03): el MIN(OccurredAtUtc) agrupado por
    /// JobId se hacía sobre TODA la tabla de eventos (todas las tiendas, todo el histórico) y
    /// luego se resolvía StoreId/AttemptCount con un segundo roundtrip + IN-list de todos los
    /// JobId impresos. El Join contra <paramref name="jobsScope"/> (ya filtrado por tienda activa
    /// y, si aplica, storeId) empuja ese scope a la agregación y evita el segundo roundtrip.
    /// </summary>
    private async Task<(List<PrintedJobRow> Printed, List<FailedJobRow> Failed)> LoadPrintedAndFailedAsync(
        IQueryable<PrintJob> jobsScope, DateTimeOffset fromUtc, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var printed = await _dbContext.PrintJobEvents
            .Where(e => e.NewStatus != null && DashboardPrintJobPredicates.PrintedStatuses.Contains(e.NewStatus.Value))
            .Join(jobsScope, e => e.JobId, j => j.JobId,
                (e, j) => new { e.JobId, j.StoreId, j.AttemptCount, e.OccurredAtUtc })
            .GroupBy(x => new { x.JobId, x.StoreId, x.AttemptCount })
            .Select(g => new { g.Key.JobId, g.Key.StoreId, g.Key.AttemptCount, FirstPrintedAtUtc = g.Min(x => x.OccurredAtUtc) })
            .Where(x => x.FirstPrintedAtUtc >= fromUtc && x.FirstPrintedAtUtc <= now)
            .Select(x => new PrintedJobRow(x.JobId, x.StoreId, x.AttemptCount))
            .ToListAsync(cancellationToken);

        var errorOrRetry = await _dbContext.PrintJobEvents
            .Where(e => (e.NewStatus == PrintJobStatus.ErrorFinal || e.NewStatus == PrintJobStatus.RetryScheduled)
                        && e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc <= now)
            .Join(jobsScope, e => e.JobId, j => j.JobId, (e, j) => new FailedJobRow(e.JobId, j.StoreId))
            .Distinct()
            .ToListAsync(cancellationToken);

        var failed = errorOrRetry
            .Concat(printed.Where(p => p.AttemptCount > 1).Select(p => new FailedJobRow(p.JobId, p.StoreId)))
            .GroupBy(x => x.JobId)
            .Select(g => g.First())
            .ToList();

        return (printed, failed);
    }

    private sealed record PrintedJobRow(Guid JobId, int StoreId, int AttemptCount);
    private sealed record FailedJobRow(Guid JobId, int StoreId);

    private async Task<(List<StoreDashboardRow> Rows, List<StoreAlert> Alerts)> BuildStoreRowsAsync(
        IQueryable<Domain.Entities.Store> stores,
        IQueryable<Domain.Entities.Printer> printers,
        IQueryable<Domain.Entities.PrintJob> jobsInWindow,
        IQueryable<Domain.Entities.PrintJob> allJobs,
        List<PrintedJobRow> printedRows,
        List<FailedJobRow> failedRows,
        DashboardThresholds thresholds,
        ThresholdRuleSet rules,
        CancellationToken cancellationToken)
    {
        var baseStores = await stores
            .Select(x => new { x.StoreId, x.Name })
            .OrderBy(x => x.StoreId)
            .ToListAsync(cancellationToken);

        var connectedPrinters = await printers
            .GroupBy(x => x.StoreId)
            .Select(x => new { StoreId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Count, cancellationToken);

        var printersConnStats = await LoadConnectivityStatsAsync(
            printers, thresholds.ConnWarningFailuresMin, thresholds.ConnCriticalFailuresMin, cancellationToken);

        var receivedStats = await jobsInWindow
            .GroupBy(x => x.StoreId)
            .Select(x => new { StoreId = x.Key, Received = x.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Received, cancellationToken);

        var printedStats = printedRows.GroupBy(p => p.StoreId).ToDictionary(g => g.Key, g => g.Count());
        var failedStats = failedRows.GroupBy(f => f.StoreId).ToDictionary(g => g.Key, g => g.Count());

        var queueCurrentStats = await allJobs
            .GroupBy(x => x.StoreId)
            .Select(x => new
            {
                StoreId = x.Key,
                QueuedCurrent = x.Count(j => DashboardPrintJobPredicates.QueueStatuses.Contains(j.Status))
            })
            .ToDictionaryAsync(x => x.StoreId, x => x.QueuedCurrent, cancellationToken);

        // Sin ventana (A-KPI-01): foto de estado actual, no filtrada por UpdatedAtUtc.
        var failedWindowStats = await allJobs
            .Where(DashboardPrintJobPredicates.FailedWithoutRetryCurrent)
            .GroupBy(x => x.StoreId)
            .Select(x => new
            {
                StoreId = x.Key,
                FailedWithoutRetryCurrent = x.Count()
            })
            .ToDictionaryAsync(x => x.StoreId, x => x.FailedWithoutRetryCurrent, cancellationToken);

        // "Desde" de las incidencias priorizadas (dashboard): antiguedad de la cola y del fallo sin
        // reenviar mas antiguo de cada tienda. Sin ventana, igual que los KPIs de estado actual que
        // acompanan (A-KPI-01): la incidencia sigue abierta aunque empezara antes del periodo.
        // La cola cuenta desde la ultima orden de imprimir, como su orden en la pantalla: un documento
        // de hace tres dias reimpreso hoy lleva minutos en cola, no tres dias.
        var oldestQueuedStats = await allJobs
            .Where(j => DashboardPrintJobPredicates.QueueStatuses.Contains(j.Status))
            .GroupBy(j => j.StoreId)
            .Select(g => new { StoreId = g.Key, Oldest = g.Min(j => j.LastRequeuedAtUtc ?? j.CreatedAtUtc) })
            .ToDictionaryAsync(x => x.StoreId, x => x.Oldest, cancellationToken);

        var oldestFailedStats = await allJobs
            .Where(DashboardPrintJobPredicates.FailedWithoutRetryCurrent)
            .GroupBy(j => j.StoreId)
            .Select(g => new { StoreId = g.Key, Oldest = g.Min(j => j.UpdatedAtUtc) })
            .ToDictionaryAsync(x => x.StoreId, x => x.Oldest, cancellationToken);

        // Breakdown por impresora: evita que el fallback PHP tenga que pedir jobs crudos
        // (api/printjobs?limit=5000, truncado a 500 por la API) solo para calcular estos chips.
        var printerQueueCurrent = await allJobs
            .Where(j => DashboardPrintJobPredicates.QueueStatuses.Contains(j.Status) && j.PrinterId != null)
            .GroupBy(j => new { j.StoreId, PrinterId = j.PrinterId!.Value })
            .Select(g => new { g.Key.StoreId, g.Key.PrinterId, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var printerFailedWindow = await allJobs
            .Where(DashboardPrintJobPredicates.FailedWithoutRetryCurrent)
            .Where(j => j.PrinterId != null)
            .GroupBy(j => new { j.StoreId, PrinterId = j.PrinterId!.Value })
            .Select(g => new { g.Key.StoreId, g.Key.PrinterId, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // "recibidos por impresora": cohorte por CreatedAtUtc, igual que `received` — no eventos.
        var printerReceivedWindow = await jobsInWindow
            .Where(j => j.PrinterId != null)
            .GroupBy(j => new { j.StoreId, PrinterId = j.PrinterId!.Value })
            .Select(g => new { g.Key.StoreId, g.Key.PrinterId, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var unassignedQueueStats = await allJobs
            .Where(j => DashboardPrintJobPredicates.QueueStatuses.Contains(j.Status) && j.PrinterId == null)
            .GroupBy(j => j.StoreId)
            .Select(g => new { StoreId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Count, cancellationToken);

        // Umbrales opcionales (27/10/2026), solo si están activados: último trabajo recibido (para
        // «sin recibir», que cuenta dentro del horario de tienda) y reimpresiones de hoy en la hora
        // de negocio (mismo corte que el KPI de reimpresiones).
        var now = _timeProvider.GetUtcNow();
        var lastReceivedStats = rules.Silent.Count == 0
            ? new Dictionary<int, DateTimeOffset>()
            : await allJobs
                .GroupBy(j => j.StoreId)
                .Select(g => new { StoreId = g.Key, Last = g.Max(j => j.CreatedAtUtc) })
                .ToDictionaryAsync(x => x.StoreId, x => x.Last, cancellationToken);
        var todayStart = BusinessTimeZoneClock.TodayStartUtc(now, _businessTimeZone);
        var reprintsTodayStats = rules.Reprints.Count == 0
            ? new Dictionary<int, int>()
            : await _dbContext.PrintJobEvents
                .Where(e => e.EventType == RoutingService.ReprintEvent && e.OccurredAtUtc >= todayStart)
                .Join(allJobs, e => e.JobId, j => j.JobId, (e, j) => j.StoreId)
                .GroupBy(storeId => storeId)
                .Select(g => new { StoreId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.StoreId, x => x.Count, cancellationToken);

        var printerAcc = new Dictionary<(int StoreId, int PrinterId), (int Queue, int Failed, int Received)>();
        foreach (var r in printerQueueCurrent)
            printerAcc[(r.StoreId, r.PrinterId)] = printerAcc.TryGetValue((r.StoreId, r.PrinterId), out var q)
                ? (q.Queue + r.Count, q.Failed, q.Received) : (r.Count, 0, 0);
        foreach (var r in printerFailedWindow)
            printerAcc[(r.StoreId, r.PrinterId)] = printerAcc.TryGetValue((r.StoreId, r.PrinterId), out var f)
                ? (f.Queue, f.Failed + r.Count, f.Received) : (0, r.Count, 0);
        foreach (var r in printerReceivedWindow)
            printerAcc[(r.StoreId, r.PrinterId)] = printerAcc.TryGetValue((r.StoreId, r.PrinterId), out var t)
                ? (t.Queue, t.Failed, t.Received + r.Count) : (0, 0, r.Count);

        // Todas las impresoras activas del ámbito, también las que no han tenido trabajos (a 0):
        // con solo las de printerAcc, una tienda sin actividad salía sin impresoras (N1).
        var activePrinterKeys = (await printers
                .Select(p => new { p.StoreId, p.PrinterId })
                .ToListAsync(cancellationToken))
            .Select(p => (p.StoreId, p.PrinterId));
        var printersByStore = activePrinterKeys
            .Union(printerAcc.Keys)
            .GroupBy(k => k.StoreId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PrinterDashboardRow>)g
                    .Select(k => printerAcc.TryGetValue(k, out var a)
                        ? new PrinterDashboardRow(k.PrinterId, a.Queue, a.Failed, a.Received)
                        : new PrinterDashboardRow(k.PrinterId, 0, 0, 0))
                    .OrderByDescending(p => p.QueueCurrent)
                    .ToList());

        var rows = new List<StoreDashboardRow>(baseStores.Count);
        var alertInputs = new List<StoreAlertInput>(baseStores.Count);
        foreach (var store in baseStores)
        {
            connectedPrinters.TryGetValue(store.StoreId, out var connected);
            printersConnStats.TryGetValue(store.StoreId, out var connStats);
            receivedStats.TryGetValue(store.StoreId, out var received);
            printedStats.TryGetValue(store.StoreId, out var printed);
            failedStats.TryGetValue(store.StoreId, out var failed);
            queueCurrentStats.TryGetValue(store.StoreId, out var queuedCurrentValue);
            failedWindowStats.TryGetValue(store.StoreId, out var failedWithoutRetryCurrentValue);
            unassignedQueueStats.TryGetValue(store.StoreId, out var unassignedQueueCurrent);
            printersByStore.TryGetValue(store.StoreId, out var storePrinters);

            var queuedCurrent = queuedCurrentValue;
            var failedWithoutRetryCurrent = failedWithoutRetryCurrentValue;

            var missingHost = connStats.MissingHost;
            var connMaxStreak = connStats.ConnMaxStreak;
            var connCrit = connStats.ConnCritical;
            var connWarn = connStats.ConnWarning;

            // Sin impresoras activas no hay nada que esperar: «sin recibir» no cuenta.
            int? silentMinutes = connected > 0 && lastReceivedStats.TryGetValue(store.StoreId, out var lastReceived)
                ? rules.Horario.MinutosSinRecibir(lastReceived, now, _businessTimeZone)
                : null;
            reprintsTodayStats.TryGetValue(store.StoreId, out var reprintsToday);

            var (health, healthReason) = StoreHealthEvaluator.Compute(
                connected, queuedCurrent, failedWithoutRetryCurrent, missingHost,
                connMaxStreak, connCrit, connWarn, rules, silentMinutes, reprintsToday);

            rows.Add(new StoreDashboardRow(
                StoreId: store.StoreId,
                StoreName: store.Name,
                ConnectedPrinters: connected,
                Received: received,
                Printed: printed,
                Failed: failed,
                QueuedCurrent: queuedCurrent,
                FailedWithoutRetryCurrent: failedWithoutRetryCurrent,
                Health: health,
                HealthReason: healthReason,
                UnassignedQueueCurrent: unassignedQueueCurrent,
                OldestQueuedAtUtc: oldestQueuedStats.TryGetValue(store.StoreId, out var oldestQueuedAt) ? oldestQueuedAt : null,
                OldestFailedWithoutRetryAtUtc: oldestFailedStats.TryGetValue(store.StoreId, out var oldestFailedAt) ? oldestFailedAt : null,
                Printers: storePrinters ?? [],
                SilentMinutes: silentMinutes,
                ReprintsToday: reprintsToday));

            alertInputs.Add(new StoreAlertInput(
                store.StoreId, store.Name, connected, queuedCurrent, failedWithoutRetryCurrent,
                missingHost, connMaxStreak, connCrit, connWarn, silentMinutes, reprintsToday));
        }

        var alerts = StoreHealthEvaluator.BuildAlerts(alertInputs, rules).ToList();
        return (rows, alerts);
    }

    /// <summary>
    /// Clasifica conectividad por impresora reutilizando <see cref="PrinterConnectivityState"/> (misma
    /// lógica que PrinterConnectivityMonitorService), y agrega por tienda (G5.3): missingHost por
    /// estado "sin host" real (no solo campo Host vacío), connCritical/connWarning con el mismo
    /// criterio OR (severidad de conectividad || streak &gt;= umbral legacy) que usaba PHP.
    /// </summary>
    private static async Task<Dictionary<int, ConnectivityStats>> LoadConnectivityStatsAsync(
        IQueryable<Domain.Entities.Printer> printers, int connWarnMin, int connCritMin, CancellationToken cancellationToken)
    {
        var rows = await printers
            .Select(p => new
            {
                p.StoreId, p.IsActive, p.LastConnectionOk, p.LastConnectionCheckAtUtc,
                p.LastConnectionError, p.ConnectionFailuresStreak
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(p => p.StoreId)
            .ToDictionary(g => g.Key, g =>
            {
                int missingHost = 0, warn = 0, crit = 0, maxStreak = 0;
                foreach (var p in g)
                {
                    var descriptor = PrinterConnectivityState.FromSnapshot(
                        p.IsActive, p.LastConnectionOk, p.LastConnectionCheckAtUtc, p.LastConnectionError, p.ConnectionFailuresStreak);

                    if (descriptor.ConnectivityStatus == PrinterConnectivityState.StatusNoHost)
                        missingHost++;

                    maxStreak = Math.Max(maxStreak, p.ConnectionFailuresStreak);

                    if (descriptor.ConnectivitySeverity == PrinterConnectivityState.SeverityCritical || p.ConnectionFailuresStreak >= connCritMin)
                        crit++;
                    else if ((descriptor.ConnectivitySeverity == PrinterConnectivityState.SeverityWarning && descriptor.ConnectivityStatus != PrinterConnectivityState.StatusNoHost)
                             || p.ConnectionFailuresStreak >= connWarnMin)
                        warn++;
                }

                return new ConnectivityStats(missingHost, warn, crit, maxStreak);
            });
    }

    private readonly record struct ConnectivityStats(int MissingHost, int ConnWarning, int ConnCritical, int ConnMaxStreak);

    private async Task<DashboardThresholds> GetThresholdsAsync(CancellationToken cancellationToken)
    {
        var row = await _dbContext.DashboardThresholds
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (row is not null)
            return MapThresholdRow(row);

        var defaults = new DashboardThresholds(
            DefaultWarningQueueMin,
            DefaultCriticalQueueMin,
            "warning",
            "critical",
            DefaultWarningFailedWithoutRetryMin,
            DefaultCriticalFailedWithoutRetryMin,
            "warning",
            "critical",
            DefaultMissingHostMin,
            "warning",
            DefaultConnWarningFailuresMin,
            DefaultConnCriticalFailuresMin,
            "warning",
            "critical");

        var defaultRow = new DashboardThreshold
        {
            Id = 1,
            WarningQueueMin = defaults.WarningQueueMin,
            CriticalQueueMin = defaults.CriticalQueueMin,
            QueueWarningSeverity = defaults.QueueWarningSeverity,
            QueueCriticalSeverity = defaults.QueueCriticalSeverity,
            WarningFailedWithoutRetryMin = defaults.WarningFailedWithoutRetryMin,
            CriticalFailedWithoutRetryMin = defaults.CriticalFailedWithoutRetryMin,
            FailedWarningSeverity = defaults.FailedWarningSeverity,
            FailedCriticalSeverity = defaults.FailedCriticalSeverity,
            MissingHostMin = defaults.MissingHostMin,
            MissingHostSeverity = defaults.MissingHostSeverity,
            ConnWarningFailuresMin = defaults.ConnWarningFailuresMin,
            ConnCriticalFailuresMin = defaults.ConnCriticalFailuresMin,
            ConnWarningSeverity = defaults.ConnWarningSeverity,
            ConnCriticalSeverity = defaults.ConnCriticalSeverity,
            UpdatedAtUtc = _timeProvider.GetUtcNow()
        };

        try
        {
            await _dbContext.DashboardThresholds.AddAsync(defaultRow, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent request inserted the singleton first; reload what it wrote.
            _dbContext.ChangeTracker.Clear();
            var concurrent = await _dbContext.DashboardThresholds
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
            if (concurrent is not null)
                return MapThresholdRow(concurrent);
            throw;
        }

        return defaults;
    }

    private static DashboardThresholds MapThresholdRow(DashboardThreshold row) =>
        new(row.WarningQueueMin,
            row.CriticalQueueMin,
            row.QueueWarningSeverity,
            row.QueueCriticalSeverity,
            row.WarningFailedWithoutRetryMin,
            row.CriticalFailedWithoutRetryMin,
            row.FailedWarningSeverity,
            row.FailedCriticalSeverity,
            row.MissingHostMin,
            row.MissingHostSeverity,
            row.ConnWarningFailuresMin,
            row.ConnCriticalFailuresMin,
            row.ConnWarningSeverity,
            row.ConnCriticalSeverity);

    private static string NormalizeSeverity(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        return v is "info" or "warning" or "critical" ? v : "warning";
    }

    private DateTimeOffset ResolveWindowStartUtc(string? window, DateTimeOffset now)
        => BusinessTimeZoneClock.WindowStartUtc(window, now, _businessTimeZone);

    private static string NormalizeWindow(string? window) => BusinessTimeZoneClock.NormalizeWindow(window);

    private int? GetCurrentUserStoreId()
    {
        var claimValue = User.FindFirstValue("StoreId");
        return int.TryParse(claimValue, out var parsed) ? parsed : null;
    }

    private sealed record StoreDashboardRow(
        int StoreId,
        string StoreName,
        int ConnectedPrinters,
        int Received,
        int Printed,
        int Failed,
        int QueuedCurrent,
        int FailedWithoutRetryCurrent,
        string Health,
        string HealthReason,
        int UnassignedQueueCurrent,
        DateTimeOffset? OldestQueuedAtUtc,
        DateTimeOffset? OldestFailedWithoutRetryAtUtc,
        IReadOnlyList<PrinterDashboardRow> Printers,
        // Umbrales opcionales: null fuera de horario o sin impresoras; reimpresiones de hoy.
        int? SilentMinutes = null,
        int ReprintsToday = 0);

    private sealed record PrinterDashboardRow(
        int PrinterId,
        int QueueCurrent,
        int FailedWindow,
        int ReceivedWindow);

    private sealed record DashboardThresholds(
        int WarningQueueMin,
        int CriticalQueueMin,
        string QueueWarningSeverity,
        string QueueCriticalSeverity,
        int WarningFailedWithoutRetryMin,
        int CriticalFailedWithoutRetryMin,
        string FailedWarningSeverity,
        string FailedCriticalSeverity,
        int MissingHostMin,
        string MissingHostSeverity,
        int ConnWarningFailuresMin,
        int ConnCriticalFailuresMin,
        string ConnWarningSeverity,
        string ConnCriticalSeverity);
}

public record UpdateDashboardThresholdsRequest(
    int WarningQueueMin,
    int CriticalQueueMin,
    int WarningFailedWithoutRetryMin,
    int CriticalFailedWithoutRetryMin,
    string? QueueWarningSeverity,
    string? QueueCriticalSeverity,
    string? FailedWarningSeverity,
    string? FailedCriticalSeverity,
    int MissingHostMin,
    string? MissingHostSeverity,
    int ConnWarningFailuresMin,
    int ConnCriticalFailuresMin,
    string? ConnWarningSeverity,
    string? ConnCriticalSeverity);

public record ThresholdRuleRequest(int Min, string? Severity);

public record ThresholdRuleSetRequest(
    List<ThresholdRuleRequest>? Queue,
    List<ThresholdRuleRequest>? Failed,
    List<ThresholdRuleRequest>? MissingHost,
    List<ThresholdRuleRequest>? Conn,
    List<ThresholdRuleRequest>? Silent = null,
    List<ThresholdRuleRequest>? Reprints = null,
    HorarioTiendaRequest? Horario = null);

public record HorarioTiendaRequest(List<int>? Dias, string? Desde, string? Hasta);
