using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Json;
using ImpresorasService.Domain.Connectivity;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
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

    public PrintersController(ImpresorasDbContext dbContext)
    {
        _dbContext = dbContext;
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

        var effectiveStoreId = IsAdmin() ? storeId : GetCurrentUserStoreId();
        if (effectiveStoreId.HasValue)
            query = query.Where(x => x.StoreId == effectiveStoreId.Value);
        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        var results = await query
            .OrderBy(x => x.StoreId)
            .ThenBy(x => x.PrinterName)
            .ToListAsync(cancellationToken);

        return Ok(results.Select(ToPrinterDto));
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
        if (!IsAdmin() && printer.StoreId != GetCurrentUserStoreId())
            return Forbid();

        return Ok(ToPrinterDto(printer));
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
            return Conflict(new { error = "Ya existe una impresora con el mismo StoreId y SpoolQueue." });
        }

        return CreatedAtAction(nameof(GetById), new { id = printer.PrinterId }, ToPrinterDto(printer));
    }

    /// <summary>
    /// Actualiza una impresora existente.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePrinterRequest request, CancellationToken cancellationToken)
    {
        // AsNoTracking: no necesitamos change-tracking; la escritura es por SQL directo
        // para eludir el bug del proveedor HANA que genera WHERE sin parametrizar strings.
        var printer = await _dbContext.Printers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PrinterId == id, cancellationToken);

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

        var now = DateTimeOffset.UtcNow;
        var nowStr = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        try
        {
            // Raw SQL garantiza parametrizado real — el proveedor HANA EF Core falla al
            // generar WHERE con strings que contienen paréntesis cuando usa LINQ.
            await _dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE \"printer_printer\" SET " +
                "\"printer_name\" = {0}, " +
                "\"spool_queue\" = {1}, " +
                "\"host\" = {2}, " +
                "\"store_id\" = {3}, " +
                "\"is_active\" = {4}, " +
                "\"capabilities_json\" = {5}, " +
                "\"updated_at_utc\" = {6} " +
                "WHERE \"printer_id\" = {7}",
                input.PrinterName,
                input.SpoolQueue,
                input.Host,
                request.StoreId,
                request.IsActive ? 1 : 0,
                input.CapabilitiesJson,
                nowStr,
                id);
        }
        catch (Exception ex) when (
            ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("constraint", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { error = "Ya existe otra impresora con el mismo StoreId y SpoolQueue." });
        }

        var updated = new Printer
        {
            PrinterId = id,
            PrinterName = input.PrinterName,
            SpoolQueue = input.SpoolQueue,
            Host = input.Host,
            StoreId = request.StoreId,
            IsActive = request.IsActive,
            CapabilitiesJson = input.CapabilitiesJson,
            CreatedAtUtc = printer.CreatedAtUtc,
            UpdatedAtUtc = now,
            ConnectionFailuresStreak = printer.ConnectionFailuresStreak,
            LastConnectionOk = printer.LastConnectionOk,
            LastConnectionCheckAtUtc = printer.LastConnectionCheckAtUtc,
            LastConnectionTransport = printer.LastConnectionTransport,
            LastConnectionError = printer.LastConnectionError,
        };
        return Ok(ToPrinterDto(updated));
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

        _dbContext.Printers.Remove(printer);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("FOREIGN KEY") == true)
        {
            return Conflict(new { error = "No se puede eliminar: existen reglas de enrutado que usan esta impresora." });
        }

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
        if (!IsAdmin() && printer.StoreId != GetCurrentUserStoreId())
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

    private static string? ExtractHostFromSpoolQueue(string spoolQueue)
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

    private async Task<bool> ActiveStoreExistsAsync(int storeId, CancellationToken cancellationToken)
    {
        if (storeId <= 0)
            return false;

        var activeStoreOnly = true;
        return (await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.IsActive == activeStoreOnly)
            .Select(x => x.StoreId)
            .Take(1)
            .ToListAsync(cancellationToken))
            .Count > 0;
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

        var normalizedSpoolQueue = spoolQueue?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSpoolQueue))
            return (null, "La cola de impresion es obligatoria.");
        if (normalizedSpoolQueue.Length > 200)
            return (null, "La cola de impresion no puede superar 200 caracteres.");

        var normalizedHost = string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        if (normalizedHost?.Length > 255)
            return (null, "El host no puede superar 255 caracteres.");

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

    private static PrinterDto ToPrinterDto(Printer printer)
    {
        var connectivity = PrinterConnectivityState.FromPrinter(printer);
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
            connectivity.ConnectivityStatus,
            connectivity.ConnectivityLabel,
            connectivity.ConnectivitySeverity,
            connectivity.ConnectivityDetail);
    }

    private sealed record PrinterDto(
        int PrinterId,
        string PrinterName,
        string SpoolQueue,
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
        string ConnectivityStatus,
        string ConnectivityLabel,
        string ConnectivitySeverity,
        string? ConnectivityDetail);

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
        string SpoolQueue,
        string? Host,
        string? CapabilitiesJson);
}

public record CreatePrinterRequest(
    string PrinterName,
    string SpoolQueue,
    string? Host,
    int StoreId,
    bool IsActive = true,
    string? CapabilitiesJson = null);

public record UpdatePrinterRequest(
    string PrinterName,
    string SpoolQueue,
    string? Host,
    int StoreId,
    bool IsActive,
    string? CapabilitiesJson = null);
