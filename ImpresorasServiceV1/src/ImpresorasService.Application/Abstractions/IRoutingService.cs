namespace ImpresorasService.Application.Abstractions;

/// <summary>
/// Servicio de enrutado: intenta resolver la impresora para un trabajo.
/// Si no hay ruta válida, aplica ErrorFinal con ROUTE_NOT_FOUND.
/// </summary>
public interface IRoutingService
{
    /// <summary>
    /// Intenta enrutar un trabajo. Si no hay regla aplicable, transiciona a ErrorFinal.
    /// </summary>
    /// <param name="jobId">ID del trabajo.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>True si se enrutó correctamente (Routed), false si se aplicó ErrorFinal (ROUTE_NOT_FOUND).</returns>
    Task<RouteResult> TryRouteJobAsync(Guid jobId, CancellationToken cancellationToken = default);
}

public record RouteResult(bool Success, int? PrinterId, string? ErrorCode);
