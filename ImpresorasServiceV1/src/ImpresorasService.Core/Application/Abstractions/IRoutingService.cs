namespace ImpresorasService.Application.Abstractions;

/// <summary>
/// Servicio de enrutado: intenta resolver la impresora para un trabajo.
/// Si no hay ruta válida, aplica ErrorFinal con ROUTE_NOT_FOUND.
/// </summary>
public interface IRoutingService
{
    /// <summary>
    /// Intenta enrutar un trabajo. Si no hay regla aplicable, transiciona a ErrorFinal.
    /// Solo funciona para trabajos en estado Pending.
    /// </summary>
    Task<RouteResult> TryRouteJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Intenta enrutar o re-enrutar. Acepta Pending o ErrorFinal (ROUTE_NOT_FOUND).
    /// Para ErrorFinal con ROUTE_NOT_FOUND, resetea a Pending y vuelve a intentar.
    /// </summary>
    Task<RouteResult> TryRetryRouteAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enruta todos los trabajos Pending del lote con una sola carga de reglas/impresoras.
    /// </summary>
    Task TryRouteBatchAsync(IReadOnlyList<Guid> jobIds, CancellationToken cancellationToken = default);
}

public record RouteResult(bool Success, int? PrinterId, string? ErrorCode);
