using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Json;
using ImpresorasService.Api.Security;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain.Connectivity;
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
public class PrintersController : ControllerBase
{
    private readonly ImpresorasDbContext _dbContext;
    private readonly IDashboardThresholdRuleStore _ruleStore;
    private readonly IIppConfirmationService _ipp;

    public PrintersController(ImpresorasDbContext dbContext, IDashboardThresholdRuleStore ruleStore,
        IIppConfirmationService ipp)
    {
        _dbContext = dbContext;
        _ruleStore = ruleStore;
        _ipp = ipp;
    }

    /// <summary>
    /// Cómo se convierte cada impresora en su DTO: con los umbrales de tóner de la pantalla Umbrales
    /// (fichero de reglas, como el dashboard) y los días que le quedan al tóner (docs/72, mejora 7).
    /// </summary>
    private async Task<Func<Printer, PrinterDto>> DtoAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var toner = (await _ruleStore.LoadAsync(ct)).Toner;
        var ahora = DateTimeOffset.UtcNow;
        // ponytail: solo lo que estima la propia Canon (su web, leída con el contador de las 00:00), menos
        // los días pasados desde entonces. Sin previsión propia: el historial de tóner se quitó el 11/11/2026.
        var desdeCanon = ahora.AddDays(-3);
        var canon = (await _dbContext.PageCounterReadings.AsNoTracking()
                .Where(r => ids.Contains(r.PrinterId) && r.TonerDaysLeft != null && r.ReadAtUtc >= desdeCanon)
                .Select(r => new { r.PrinterId, r.ReadAtUtc, r.TonerDaysLeft })
                .ToListAsync(ct))
            .GroupBy(r => r.PrinterId)
            .ToDictionary(g => g.Key, g => g.MaxBy(r => r.ReadAtUtc)!)
            .ToDictionary(x => x.Key, x => x.Value.TonerDaysLeft >= LecturaContador.MasDeUnAno
                ? LecturaContador.MasDeUnAno
                : Math.Max(0, x.Value.TonerDaysLeft!.Value - (int)(ahora - x.Value.ReadAtUtc).TotalDays));
        return p => canon.TryGetValue(p.PrinterId, out var deCanon)
            ? ToPrinterDto(p, toner, deCanon, "impresora")
            : ToPrinterDto(p, toner, null, null);
    }

    /// <summary>
    /// Lista impresoras, opcionalmente filtradas por tienda.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? storeId,
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        IQueryable<Printer> query = _dbContext.Printers.AsNoTracking();

        var effectiveStoreId = User.VeTodasLasTiendas() ? storeId : GetCurrentUserStoreId();
        if (!User.VeTodasLasTiendas() && !effectiveStoreId.HasValue)
            return Forbid();
        if (effectiveStoreId.HasValue)
            query = query.Where(x => x.StoreId == effectiveStoreId.Value);
        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        var results = await query
            .OrderBy(x => x.StoreId)
            .ThenBy(x => x.PrinterName)
            .ToListAsync(cancellationToken);

        var dto = await DtoAsync(results.Select(p => p.PrinterId).ToList(), cancellationToken);
        return Ok(results.Select(dto));
    }

    /// <summary>
    /// Obtiene una impresora por ID.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var printer = await _dbContext.Printers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PrinterId == id, cancellationToken);

        if (printer is null)
            return NotFound();
        if (!User.VeTodasLasTiendas() && printer.StoreId != GetCurrentUserStoreId())
            return Forbid();

        return Ok((await DtoAsync([printer.PrinterId], cancellationToken))(printer));
    }

    // ponytail: dos años por consulta; con más, varias consultas.
    private const int MaxDiasPaginas = 731;

    /// <summary>
    /// Páginas impresas por impresora del <paramref name="from"/> al <paramref name="to"/>, ambos incluidos,
    /// a partir de la lectura diaria del contador (ERP_SERCA docs/73, opción E). Solo administración.
    /// </summary>
    [HttpGet("page-counts")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> PageCounts(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int? printerId,
        CancellationToken cancellationToken)
    {
        if (from == default || to == default || to < from)
            return BadRequest(new { error = "Indique las dos fechas, la primera no posterior a la segunda." });
        if (to.DayNumber - from.DayNumber >= MaxDiasPaginas)
            return BadRequest(new { error = "Como mucho dos años por consulta." });

        IQueryable<Printer> query = _dbContext.Printers.AsNoTracking();
        if (printerId.HasValue)
            query = query.Where(x => x.PrinterId == printerId.Value);
        if (!User.VeTodasLasTiendas())
        {
            var storeId = GetCurrentUserStoreId();
            if (!storeId.HasValue)
                return Forbid();
            query = query.Where(x => x.StoreId == storeId.Value);
        }
        var printers = await query.OrderBy(x => x.StoreId).ThenBy(x => x.PrinterName).ToListAsync(cancellationToken);
        var lecturas = await ContadorPaginas.LecturasAsync(
            _dbContext, printers.Select(p => p.PrinterId).ToList(), from, to, cancellationToken);

        return Ok(printers
            .Select(p => (p, periodos: ContadorPaginas.Periodos(lecturas[p.PrinterId], from, to)))
            // Las inactivas, solo si tienen lecturas en el rango.
            .Where(x => x.p.IsActive || x.periodos.Count > 0)
            .Select(x => new PageCountDto(
                x.p.PrinterId,
                x.p.PrinterName,
                x.p.StoreId,
                x.p.IsActive,
                x.periodos.Count > 0 ? x.periodos.Sum(d => d.Paginas ?? 0) : null,
                lecturas[x.p.PrinterId].MaxBy(r => r.BusinessDay)?.Source,
                x.periodos.Select(d => new PageCountPeriodDto(
                    d.Desde, d.Hasta, d.Paginas, d.LeidaDesdeUtc, d.LeidaHastaUtc)).ToList())));
    }

    /// <summary>
    /// Crea una impresora. La combinación StoreId + SpoolQueue debe ser única.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] CreatePrinterRequest request, CancellationToken cancellationToken)
    {
        var (input, validationError) = NormalizePrinterInput(
            request.PrinterName,
            request.SpoolQueue,
            request.Host,
            request.CapabilitiesJson);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        if (input is null)
            return BadRequest(new { error = "La impresora no es valida." });

        if (!await ActiveStoreExistsAsync(request.StoreId, cancellationToken))
            return BadRequest(new { error = "La tienda especificada no existe o esta inactiva." });

        if (await DuplicadaAsync(null, input, request.StoreId, request.IsActive, cancellationToken) is { } duplicada)
            return Conflict(new { error = duplicada });

        var printer = new Printer
        {
            PrinterName = input.PrinterName,
            SpoolQueue = input.SpoolQueue,
            Host = input.Host,
            StoreId = request.StoreId,
            IsActive = request.IsActive,
            CapabilitiesJson = input.CapabilitiesJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.Printers.Add(printer);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "Ya hay una impresora con esa cola de Windows en la tienda." });
        }

        return CreatedAtAction(nameof(GetById), new { id = printer.PrinterId }, (await DtoAsync([printer.PrinterId], cancellationToken))(printer));
    }

    /// <summary>
    /// Actualiza una impresora existente.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePrinterRequest request, CancellationToken cancellationToken)
    {
        var printer = await _dbContext.Printers.FirstOrDefaultAsync(x => x.PrinterId == id, cancellationToken);

        if (printer is null)
            return NotFound();

        var (input, validationError) = NormalizePrinterInput(
            request.PrinterName,
            request.SpoolQueue,
            request.Host,
            request.CapabilitiesJson);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        if (input is null)
            return BadRequest(new { error = "La impresora no es valida." });

        if (!await ActiveStoreExistsAsync(request.StoreId, cancellationToken))
            return BadRequest(new { error = "La tienda especificada no existe o esta inactiva." });

        if (await DuplicadaAsync(id, input, request.StoreId, request.IsActive, cancellationToken) is { } duplicada)
            return Conflict(new { error = duplicada });

        // Las reglas de una tienda solo mandan a impresoras suyas (se comprueba al crearlas): moverla
        // de tienda haría que esas reglas imprimieran en otra.
        if (request.StoreId != printer.StoreId
            && await _dbContext.RoutingRules.AnyAsync(
                r => r.PrinterId == id && r.StoreId != null && r.StoreId != request.StoreId, cancellationToken))
            return Conflict(new { error = "Tiene reglas de enrutado de su tienda: cámbialas o bórralas antes de pasarla a otra." });

        // Con host nuevo, el Worker vuelve a descubrir el soporte IPP en el siguiente ciclo, y el
        // estado leído era de otra impresora.
        if (!string.Equals(printer.Host, input.Host, StringComparison.OrdinalIgnoreCase))
        {
            printer.IppSupported = null;
            IppEstadoImpresora.AplicarA(printer, null, null);
        }

        printer.PrinterName = input.PrinterName;
        printer.SpoolQueue = input.SpoolQueue;
        printer.Host = input.Host;
        printer.StoreId = request.StoreId;
        printer.IsActive = request.IsActive;
        printer.CapabilitiesJson = input.CapabilitiesJson;
        printer.UpdatedAtUtc = DateTimeOffset.UtcNow;

        // EF solo escribe las columnas que cambian: la telemetría de conectividad que actualiza el
        // Worker en paralelo no se pisa.
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "Ya hay otra impresora con esa cola de Windows en la tienda." });
        }

        return Ok((await DtoAsync([printer.PrinterId], cancellationToken))(printer));
    }

    /// <summary>
    /// Elimina una impresora. Fallará si hay reglas de enrutado que la referencian.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var printer = await _dbContext.Printers.FindAsync(new object[] { id }, cancellationToken);

        if (printer is null)
            return NotFound();

        // La base no tiene claves foráneas (scripts/sql/crear-esquema.sql): sin esta comprobación
        // las reglas se quedaban apuntando a una impresora borrada.
        if (await _dbContext.RoutingRules.AnyAsync(r => r.PrinterId == id, cancellationToken))
            return Conflict(new { error = "No se puede eliminar: existen reglas de enrutado que usan esta impresora." });

        _dbContext.Printers.Remove(printer);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Comprueba conectividad al host extraído del SpoolQueue (UNC \\host\share).
    /// En vez de ICMP (ping), usa TCP a 445 (SMB) para funcionar aunque ICMP esté bloqueado.
    /// Devuelve reachable, latencyMs y opcionalmente error.
    /// </summary>
    [HttpPost("{id:int}/ping")]
    public Task<IActionResult> Ping(int id, CancellationToken cancellationToken)
        => CheckSpoolQueueConnectivityAsync(id, persist: false, cancellationToken);

    /// <summary>
    /// Comprueba conectividad al host extraído del SpoolQueue (UNC \\host\share) vía TCP/445 (SMB).
    /// Equivalente funcional a <see cref="Ping(int, CancellationToken)"/>, pero con nombre semántico.
    /// </summary>
    [HttpPost("{id:int}/netconnection")]
    public Task<IActionResult> NetConnection(
        int id,
        [FromQuery] bool persist,
        CancellationToken cancellationToken)
        => CheckSpoolQueueConnectivityAsync(id, persist, cancellationToken);

    private async Task<IActionResult> CheckSpoolQueueConnectivityAsync(
        int id,
        bool persist,
        CancellationToken cancellationToken)
    {
        var isAdmin = IsAdmin();

        var printer = await _dbContext.Printers
            .FirstOrDefaultAsync(x => x.PrinterId == id, cancellationToken);

        if (printer is null)
            return NotFound();
        if (!User.VeTodasLasTiendas() && printer.StoreId != GetCurrentUserStoreId())
            return Forbid();

        var host = !string.IsNullOrWhiteSpace(printer.Host)
            ? ExtractHostFromMaybeUnc(printer.Host)
            : ExtractHostFromSpoolQueue(printer.SpoolQueue);
        if (string.IsNullOrEmpty(host))
        {
            var result = ConnectivityProbeResult.NoHost(
                isAdmin
                    ? "Host no configurado. Indica 'Host' en la impresora o usa 'SpoolQueue' en formato UNC (\\\\host\\share)."
                    : null);

            await PersistConnectivityIfRequestedAsync(printer, result, persist, cancellationToken);
            return Ok(ToConnectivityResponse(result, isAdmin));
        }

        try
        {
            // Probamos puertos típicos según cómo Windows acceda a la impresora:
            // - 515: LPR/LPD (muy común cuando el puerto TCP/IP está en modo LPR)
            // - 9100: RAW JetDirect / AppSocket
            // - 631: IPP (a veces IPPS=443, pero aquí comprobamos IPP estándar)
            // - 445: SMB (impresora compartida en Windows)
            // - 139: SMB legacy (NetBIOS/Session)
            var portsToTry = new[] { 515, 9100, 631, 445, 139 };
            const int timeoutMsPerPort = 1500;
            string? lastErrorDetail = null;

            foreach (var port in portsToTry)
            {
                var sw = Stopwatch.StartNew();
                using var tcp = new TcpClient();

                var connectTask = tcp.ConnectAsync(host, port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMsPerPort, cancellationToken));

                if (!ReferenceEquals(completed, connectTask))
                {
                    _ = connectTask.ContinueWith(
                        t => { _ = t.Exception; },
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                    continue;
                }

                try
                {
                    await connectTask;
                    var result = ConnectivityProbeResult.Ok((int)sw.ElapsedMilliseconds, $"tcp/{port}");
                    // «Comprobar» también lee el estado IPP, sin esperar al Worker (docs/72, mejora 1).
                    if (persist)
                        await LeerEstadoIppAsync(printer, host, cancellationToken);
                    await PersistConnectivityIfRequestedAsync(printer, result, persist, cancellationToken);
                    return Ok(ToConnectivityResponse(result, isAdmin));
                }
                catch (Exception ex)
                {
                    lastErrorDetail = ex.Message;
                }
            }

            var failedResult = ConnectivityProbeResult.Failed(
                isAdmin
                    ? $"No se pudo conectar a {host} (puertos TCP: {string.Join(", ", portsToTry)})"
                        + (string.IsNullOrWhiteSpace(lastErrorDetail) ? string.Empty : $". Ultimo error: {lastErrorDetail}")
                    : null);
            await PersistConnectivityIfRequestedAsync(printer, failedResult, persist, cancellationToken);
            return Ok(ToConnectivityResponse(failedResult, isAdmin));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var result = ConnectivityProbeResult.Failed(isAdmin ? ex.Message : null);
            await PersistConnectivityIfRequestedAsync(printer, result, persist, cancellationToken);
            return Ok(ToConnectivityResponse(result, isAdmin));
        }
    }

    // Lo mismo que hace el Worker en cada ciclo: estado, desde cuándo y lecturas de tóner. Sin respuesta
    // IPP no se toca nada (ni ipp_supported: lo decide el Worker con sus puertos).
    private async Task LeerEstadoIppAsync(Printer printer, string host, CancellationToken ct)
    {
        var r = await _ipp.QueryPrinterStateAsync(host, ct);
        if (r.Estado is not { } estado)
            return;
        var ahora = DateTimeOffset.UtcNow;
        IppEstadoImpresora.AplicarA(printer, estado, ahora,
            estado.Desde(printer.IppState, printer.IppStateReasons, printer.IppStateSinceUtc, ahora));
        printer.IppSupported = true;
    }

    /// <summary>
    /// Identify-Printer: la impresora pita o parpadea para encontrarla (docs/72, mejora 4). Para quien
    /// puede gestionar la tienda, como reimprimir.
    /// </summary>
    [HttpPost("{id:int}/identify")]
    [Authorize(Policy = "StoreManagerOrAdmin")]
    public async Task<IActionResult> Identify(int id, CancellationToken cancellationToken)
    {
        var printer = await _dbContext.Printers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PrinterId == id, cancellationToken);
        if (printer is null)
            return NotFound();
        if (!User.VeTodasLasTiendas() && printer.StoreId != GetCurrentUserStoreId())
            return Forbid();

        var host = !string.IsNullOrWhiteSpace(printer.Host) ? ExtractHostFromMaybeUnc(printer.Host) : null;
        if (string.IsNullOrEmpty(host))
            return Ok(new { identified = (bool?)null, message = "La impresora no tiene IP: no se puede identificar." });

        var r = await _ipp.IdentifyAsync(host, cancellationToken);
        return Ok(new
        {
            identified = r,
            message = r switch
            {
                true => "La impresora ha recibido el aviso: debería pitar o parpadear.",
                false => "La impresora responde pero no admite identificarse.",
                null => "La impresora no responde por IPP."
            }
        });
    }

    /// <summary>
    /// La cola de la propia impresora (Get-Jobs, ERP_SERCA docs/72, R3): lo que tiene sin terminar. Los que
    /// mandó este servicio salen con su documento; de los demás, el nombre y el usuario solo los ve quien
    /// administra (D-72.6: pueden ser de otros equipos).
    /// </summary>
    [HttpGet("{id:int}/jobs")]
    public async Task<IActionResult> Jobs(int id, CancellationToken cancellationToken)
    {
        var printer = await _dbContext.Printers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PrinterId == id, cancellationToken);
        if (printer is null)
            return NotFound();
        if (!User.VeTodasLasTiendas() && printer.StoreId != GetCurrentUserStoreId())
            return Forbid();

        var host = !string.IsNullOrWhiteSpace(printer.Host) ? ExtractHostFromMaybeUnc(printer.Host) : null;
        if (string.IsNullOrEmpty(host))
            return Ok(new ColaImpresoraDto(false, "La impresora no tiene IP: no se puede consultar su cola.", []));

        var trabajos = await _ipp.GetJobsAsync(host, cancellationToken);
        if (trabajos is null)
            return Ok(new ColaImpresoraDto(false, "La impresora no responde por IPP.", []));

        var numeros = trabajos.Select(t => t.JobId).ToList();
        var desde = DateTimeOffset.UtcNow.AddDays(-3);
        var nuestros = await _dbContext.PrintJobs.AsNoTracking()
            .Where(j => j.PrinterId == id && j.IppJobId != null && numeros.Contains(j.IppJobId.Value) && j.CreatedAtUtc >= desde)
            .Select(j => new { IppJobId = j.IppJobId!.Value, j.ExternalJobId, j.DocumentType, j.Status })
            .ToListAsync(cancellationToken);
        var admin = User.IsInRole(RoleCatalog.Admin);

        return Ok(new ColaImpresoraDto(true, null, trabajos.Select(t =>
        {
            var nuestro = nuestros.FirstOrDefault(n => n.IppJobId == t.JobId);
            return new TrabajoImpresoraDto(
                t.JobId,
                nuestro is not null || admin ? t.Nombre : null,
                t.Estado,
                t.Motivos,
                nuestro is not null || admin ? t.Usuario : null,
                printer.IppUpSinceUtc is { } encendida && t.CreadoSegundos is { } s ? encendida.AddSeconds(s) : null,
                nuestro is null ? null : new TrabajoNuestroDto(nuestro.ExternalJobId, nuestro.DocumentType, (int)nuestro.Status));
        }).ToList()));
    }

    private sealed record ColaImpresoraDto(bool Respondio, string? Mensaje, IReadOnlyList<TrabajoImpresoraDto> Trabajos);
    private sealed record TrabajoImpresoraDto(int JobId, string? Nombre, int? Estado, string? Motivos, string? Usuario,
        DateTimeOffset? CreadoUtc, TrabajoNuestroDto? Nuestro);
    private sealed record TrabajoNuestroDto(string ExternalJobId, string DocumentType, int Status);

    private async Task PersistConnectivityIfRequestedAsync(
        Printer printer,
        ConnectivityProbeResult result,
        bool persist,
        CancellationToken cancellationToken)
    {
        if (!persist)
            return;

        PrinterConnectivityState.ApplyProbeResult(
            printer,
            result.Reachable,
            result.Transport,
            result.Error,
            DateTimeOffset.UtcNow);

        printer.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static object ToConnectivityResponse(ConnectivityProbeResult result, bool isAdmin)
    {
        var label = !isAdmin && result.ConnectivityStatus == PrinterConnectivityState.StatusError
            ? "Sin conexion"
            : result.ConnectivityLabel;

        return new
        {
            reachable = result.Reachable,
            latencyMs = result.LatencyMs,
            transport = result.Transport,
            error = result.Error,
            detail = isAdmin ? result.Detail : null,
            connectivityStatus = result.ConnectivityStatus,
            connectivityLabel = label,
            connectivitySeverity = result.ConnectivitySeverity
        };
    }

    private static string? ExtractHostFromMaybeUnc(string hostOrUnc)
    {
        if (string.IsNullOrWhiteSpace(hostOrUnc))
            return null;

        // Acepta:
        // - "192.168.1.10"
        // - "server"
        // - "\\server\share" (o incluso con espacios, etc.)
        var trimmed = hostOrUnc.Trim();

        if (trimmed.StartsWith("\\\\", StringComparison.Ordinal))
        {
            trimmed = trimmed.TrimStart('\\');
            var parts = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 1 ? parts[0].Trim() : null;
        }

        // Caso: "server\share" (sin los \\ iniciales)
        if (trimmed.Contains('\\'))
        {
            var parts = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 1 ? parts[0].Trim() : null;
        }

        return trimmed;
    }

    private static string? ExtractHostFromSpoolQueue(string? spoolQueue)
    {
        if (string.IsNullOrWhiteSpace(spoolQueue))
            return null;

        // Solo extraemos host si es UNC: \\host\share o \\192.168.1.10\share.
        // Si es un nombre de cola local (ej. "Microsoft Print to PDF"), no tiene host asociado.
        if (spoolQueue.Length < 2 || spoolQueue[0] != '\\' || spoolQueue[1] != '\\')
            return null;

        var parts = spoolQueue.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 1 ? parts[0] : null;
    }

    private bool IsAdmin() => User.IsInRole("Admin");

    // Se imprime por ipp://{host}:631: con «.», «http://…» o «10.0.0.5:9100» la impresora no recibía
    // nada. Lo que solo tiene cifras y puntos ha de ser una IPv4 entera (CheckHostName da por buen
    // nombre «10.0.0» o «192.168.1.300»).
    internal static bool HostValido(string? host) =>
        host is not null
        && (host.All(c => char.IsAsciiDigit(c) || c == '.')
            ? host.Split('.').Length == 4 && IPAddress.TryParse(host, out _)
            : Uri.CheckHostName(host) != UriHostNameType.Unknown);

    // Otra impresora activa con la misma IP (dos fichas para un aparato reparten los trabajos y
    // duplican los avisos) o con el mismo nombre en la tienda (en las reglas no se distinguen). Las
    // inactivas no cuentan: al sustituir una, la vieja se queda de baja con su IP.
    private async Task<string?> DuplicadaAsync(
        int? id, NormalizedPrinterInput input, int storeId, bool isActive, CancellationToken cancellationToken)
    {
        if (!isActive)
            return null;

        var activas = await _dbContext.Printers
            .AsNoTracking()
            .Where(p => p.IsActive && (id == null || p.PrinterId != id))
            .Select(p => new { p.PrinterName, p.StoreId, p.Host })
            .ToListAsync(cancellationToken);

        static string? Ip(string? host) => host is null ? null : ExtractHostFromMaybeUnc(host)?.ToLowerInvariant();
        var ip = Ip(input.Host);
        var mismaIp = ip is null ? null : activas.FirstOrDefault(p => Ip(p.Host) == ip);
        if (mismaIp is not null)
            return $"La IP {input.Host} ya es de la impresora «{mismaIp.PrinterName}» (tienda {mismaIp.StoreId}).";

        var mismoNombre = activas.FirstOrDefault(p =>
            p.StoreId == storeId && string.Equals(p.PrinterName.Trim(), input.PrinterName, StringComparison.OrdinalIgnoreCase));
        return mismoNombre is null ? null : $"Ya hay una impresora «{mismoNombre.PrinterName}» en la tienda {storeId}.";
    }

    private async Task<bool> ActiveStoreExistsAsync(int storeId, CancellationToken cancellationToken)
    {
        if (storeId < 0)
            return false;

        return await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.IsActive)
            .AnyAsync(cancellationToken);
    }

    private static (NormalizedPrinterInput? Input, string? Error) NormalizePrinterInput(
        string? printerName,
        string? spoolQueue,
        string? host,
        string? capabilitiesJson)
    {
        var normalizedName = printerName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            return (null, "El nombre de impresora es obligatorio.");
        if (normalizedName.Length > 120)
            return (null, "El nombre de impresora no puede superar 120 caracteres.");

        // Se imprime primero por IP (host) y la cola de Windows es la alternativa: basta una de las dos.
        var normalizedSpoolQueue = string.IsNullOrWhiteSpace(spoolQueue) ? null : spoolQueue.Trim();
        if (normalizedSpoolQueue?.Length > 200)
            return (null, "La cola de impresion no puede superar 200 caracteres.");

        var normalizedHost = string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        if (normalizedHost?.Length > 255)
            return (null, "El host no puede superar 255 caracteres.");
        if (normalizedHost is not null && !HostValido(ExtractHostFromMaybeUnc(normalizedHost)))
            return (null, "La IP no es válida: escribe solo la IP o el nombre de la impresora, sin http://, puerto ni ruta.");
        if (normalizedSpoolQueue is null && normalizedHost is null)
            return (null, "Indica la IP de la impresora o su cola de Windows.");

        var normalizedCapabilitiesJson = string.IsNullOrWhiteSpace(capabilitiesJson) ? null : capabilitiesJson.Trim();
        if (normalizedCapabilitiesJson is not null)
        {
            try
            {
                using var _ = JsonDocument.Parse(normalizedCapabilitiesJson);
            }
            catch (JsonException)
            {
                return (null, "CapabilitiesJson no contiene JSON valido.");
            }
        }

        return (new NormalizedPrinterInput(
            normalizedName,
            normalizedSpoolQueue,
            normalizedHost,
            normalizedCapabilitiesJson), null);
    }

    private int? GetCurrentUserStoreId()
    {
        var claimValue = User.FindFirstValue("StoreId");
        return int.TryParse(claimValue, out var storeId) ? storeId : null;
    }

    private static PrinterDto ToPrinterDto(Printer printer, UmbralToner toner, int? tonerDaysLeft,
        string? tonerDaysSource = null)
    {
        var connectivity = PrinterConnectivityState.FromPrinter(printer);
        var (ippSeverity, ippStateText) = IppEstadoImpresora.Describir(printer, DateTimeOffset.UtcNow, toner);
        return new PrinterDto(
            printer.PrinterId,
            printer.PrinterName,
            printer.SpoolQueue,
            printer.Host,
            printer.StoreId,
            printer.IsActive,
            printer.CapabilitiesJson,
            printer.CreatedAtUtc,
            printer.UpdatedAtUtc,
            printer.ConnectionFailuresStreak,
            printer.LastConnectionOk,
            printer.LastConnectionCheckAtUtc,
            printer.LastConnectionTransport,
            printer.LastConnectionError,
            printer.ConnectionFailingSinceUtc,
            connectivity.ConnectivityStatus,
            connectivity.ConnectivityLabel,
            connectivity.ConnectivitySeverity,
            connectivity.ConnectivityDetail,
            printer.IppSupported,
            printer.IppState,
            printer.IppStateReasons,
            printer.IppStateMessage,
            printer.IppQueuedJobs,
            printer.IppMarkersJson is null
                ? null
                : JsonSerializer.Deserialize<List<IppConsumible>>(printer.IppMarkersJson, JsonWeb),
            printer.IppMakeAndModel,
            printer.IppMoreInfoUrl,
            printer.IppCheckedAtUtc,
            ippSeverity,
            ippStateText,
            toner.Aviso,
            toner.Critico,
            printer.IppStateSinceUtc,
            printer.IppFirmware,
            printer.IppUpSinceUtc,
            printer.IppTraysJson is null
                ? null
                : JsonSerializer.Deserialize<List<IppBandeja>>(printer.IppTraysJson, JsonWeb),
            tonerDaysLeft,
            tonerDaysSource);
    }

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private sealed record PrinterDto(
        int PrinterId,
        string PrinterName,
        string? SpoolQueue,
        string? Host,
        int StoreId,
        bool IsActive,
        string? CapabilitiesJson,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        int ConnectionFailuresStreak,
        bool? LastConnectionOk,
        DateTimeOffset? LastConnectionCheckAtUtc,
        string? LastConnectionTransport,
        string? LastConnectionError,
        DateTimeOffset? ConnectionFailingSinceUtc,
        string ConnectivityStatus,
        string ConnectivityLabel,
        string ConnectivitySeverity,
        string? ConnectivityDetail,
        bool? IppSupported,
        int? IppState,
        string? IppStateReasons,
        string? IppStateMessage,
        int? IppQueuedJobs,
        IReadOnlyList<IppConsumible>? IppMarkers,
        string? IppMakeAndModel,
        string? IppMoreInfoUrl,
        DateTimeOffset? IppCheckedAtUtc,
        // Gravedad y texto en castellano de la última lectura (IppEstadoImpresora.Describir).
        string IppSeverity,
        string? IppStateText,
        // Umbrales de tóner con que se ha calculado: la barra se pinta con ellos.
        int TonerWarningPct,
        int TonerCriticalPct,
        // docs/72, mejoras 3, 6 y 7: desde cuándo dura el estado, ficha y días hasta el tóner crítico.
        DateTimeOffset? IppStateSinceUtc,
        string? IppFirmware,
        DateTimeOffset? IppUpSinceUtc,
        IReadOnlyList<IppBandeja>? IppTrays,
        int? TonerDaysLeft,
        // De dónde salen: «impresora» (lo que estima la Canon, hasta agotarse; 366 = más de un año).
        // Sin estimación, null.
        string? TonerDaysSource);

    private sealed record ConnectivityProbeResult(
        bool Reachable,
        int? LatencyMs,
        string? Transport,
        string? Error,
        string? Detail,
        string ConnectivityStatus,
        string ConnectivityLabel,
        string ConnectivitySeverity)
    {
        public static ConnectivityProbeResult Ok(int latencyMs, string transport)
            => new(
                true,
                latencyMs,
                transport,
                null,
                null,
                PrinterConnectivityState.StatusOk,
                "OK",
                PrinterConnectivityState.SeverityHealthy);

        public static ConnectivityProbeResult NoHost(string? detail)
            => new(
                false,
                null,
                null,
                PrinterConnectivityState.HostNotConfiguredError,
                detail,
                PrinterConnectivityState.StatusNoHost,
                "Sin host",
                PrinterConnectivityState.SeverityWarning);

        public static ConnectivityProbeResult Failed(string? detail)
            => new(
                false,
                null,
                null,
                PrinterConnectivityState.NoConnectionError,
                detail,
                PrinterConnectivityState.StatusError,
                "Error",
                PrinterConnectivityState.SeverityCritical);
    }

    private sealed record NormalizedPrinterInput(
        string PrinterName,
        string? SpoolQueue,
        string? Host,
        string? CapabilitiesJson);
}

/// <summary>
/// Páginas de una impresora en el rango pedido: <see cref="Pages"/> suma los periodos que se saben (null
/// sin lecturas). Cada periodo va de la lectura de las 00:00 de <see cref="PageCountPeriodDto.From"/> a la
/// de <see cref="PageCountPeriodDto.To"/> (excluido): normalmente un día; más si faltó alguna lectura.
/// </summary>
public sealed record PageCountDto(
    int PrinterId,
    string PrinterName,
    int StoreId,
    bool IsActive,
    long? Pages,
    string? Source,
    IReadOnlyList<PageCountPeriodDto> Periods);

/// <summary>Pages null: el contador bajó (impresora cambiada o reiniciada).</summary>
public sealed record PageCountPeriodDto(
    DateOnly From,
    DateOnly To,
    long? Pages,
    DateTimeOffset FromReadAtUtc,
    DateTimeOffset ToReadAtUtc);

public record CreatePrinterRequest(
    string PrinterName,
    string? SpoolQueue,
    string? Host,
    int StoreId,
    bool IsActive = true,
    string? CapabilitiesJson = null);

public record UpdatePrinterRequest(
    string PrinterName,
    string? SpoolQueue,
    string? Host,
    int StoreId,
    bool IsActive,
    string? CapabilitiesJson = null);
