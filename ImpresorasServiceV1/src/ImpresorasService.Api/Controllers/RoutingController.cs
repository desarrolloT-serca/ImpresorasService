using ImpresorasService.Application.Abstractions;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RoutingController : ControllerBase
{
    private readonly IRoutingResolver _resolver;
    private readonly ImpresorasDbContext _db;

    public RoutingController(IRoutingResolver resolver, ImpresorasDbContext db)
    {
        _resolver = resolver;
        _db = db;
    }

    /// <summary>
    /// Resuelve la impresora para un trabajo según reglas activas.
    /// Orden: StoreId+DocumentType+Channel > StoreId+DocumentType > StoreId > Global.
    /// </summary>
    [HttpPost("resolve")]
    public async Task<IActionResult> Resolve([FromBody] ResolveRoutingRequest request, CancellationToken cancellationToken)
    {
        var printerId = await _resolver.ResolvePrinterAsync(
            request.StoreId,
            request.DocumentType,
            request.Channel ?? "DEFAULT",
            cancellationToken);

        if (printerId is null)
            return NotFound(new { error = "No hay regla aplicable para este trabajo.", code = "ROUTE_NOT_FOUND" });

        var printer = await _db.Printers
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PrinterId == printerId.Value, cancellationToken);

        if (printer is null)
            return NotFound(new { error = "Impresora no encontrada." });

        return Ok(new { printerId = printer.PrinterId, printer });
    }
}

public record ResolveRoutingRequest(int StoreId, string DocumentType, string? Channel = "DEFAULT");
