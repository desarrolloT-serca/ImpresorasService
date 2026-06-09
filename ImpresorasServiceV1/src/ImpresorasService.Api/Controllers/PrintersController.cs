using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Json;
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

        return Ok(results);
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

        return Ok(printer);
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

        var exists = (await _dbContext.Printers
            .AsNoTracking()
            .Where(x => x.StoreId == request.StoreId && x.SpoolQueue == input.SpoolQueue)
            .Select(x => x.PrinterId)
            .Take(1)
            .ToListAsync(cancellationToken))
            .Count > 0;

        if (exists)
            return Conflict(new { error = "Ya existe una impresora con el mismo StoreId y SpoolQueue." });

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
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = printer.PrinterId }, printer);
    }

    /// <summary>
    /// Actualiza una impresora existente.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePrinterRequest request, CancellationToken cancellationToken)
    {
        var printer = await _dbContext.Printers.FindAsync(new object[] { id }, cancellationToken);

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

        var duplicate = (await _dbContext.Printers
            .AsNoTracking()
            .Where(x => x.StoreId == request.StoreId && x.SpoolQueue == input.SpoolQueue && x.PrinterId != id)
            .Select(x => x.PrinterId)
            .Take(1)
            .ToListAsync(cancellationToken))
            .Count > 0;

        if (duplicate)
            return Conflict(new { error = "Ya existe otra impresora con el mismo StoreId y SpoolQueue." });

        printer.PrinterName = input.PrinterName;
        printer.SpoolQueue = input.SpoolQueue;
        printer.Host = input.Host;
        printer.StoreId = request.StoreId;
        printer.IsActive = request.IsActive;
        printer.CapabilitiesJson = input.CapabilitiesJson;
        printer.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(printer);
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
        => CheckSpoolQueueConnectivityAsync(id, cancellationToken);

    /// <summary>
    /// Comprueba conectividad al host extraído del SpoolQueue (UNC \\host\share) vía TCP/445 (SMB).
    /// Equivalente funcional a <see cref="Ping(int, CancellationToken)"/>, pero con nombre semántico.
    /// </summary>
    [HttpPost("{id:int}/netconnection")]
    public Task<IActionResult> NetConnection(int id, CancellationToken cancellationToken)
        => CheckSpoolQueueConnectivityAsync(id, cancellationToken);

    private async Task<IActionResult> CheckSpoolQueueConnectivityAsync(int id, CancellationToken cancellationToken)
    {
        var isAdmin = IsAdmin();

        var printer = await _dbContext.Printers
            .AsNoTracking()
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
            return Ok(new
            {
                reachable = false,
                error = isAdmin
                    ? "Host no configurado. Indica 'Host' en la impresora o usa 'SpoolQueue' en formato UNC (\\\\host\\share)."
                    : "Configuracion de conectividad pendiente."
            });
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

                await connectTask;
                return Ok(new
                {
                    reachable = true,
                    latencyMs = (int)sw.ElapsedMilliseconds,
                    transport = $"tcp/{port}"
                });
            }

            return Ok(new
            {
                reachable = false,
                error = isAdmin
                    ? $"No se pudo conectar a {host} (puertos TCP: {string.Join(", ", portsToTry)})"
                    : "Sin conexion con la impresora."
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                reachable = false,
                error = isAdmin ? ex.Message : "Error de conectividad con la impresora."
            });
        }
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

        return (await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.IsActive)
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
