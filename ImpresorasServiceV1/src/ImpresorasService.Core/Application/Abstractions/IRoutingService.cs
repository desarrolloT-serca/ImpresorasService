namespace ImpresorasService.Application.Abstractions;

/// <summary>
/// Servicio de enrutado: intenta resolver la impresora para un trabajo.
/// Si no hay ruta válida, aplica ErrorFinal con ROUTE_NOT_FOUND.
/// </summary>
public interface IRoutingService
{
    /// <summary>
    /// Reintento de algo que no ha salido: acepta Pending o ErrorFinal, que resetea a Pending y
    /// vuelve a enrutar. Lo que ya salió va por <see cref="ReprintAsync"/>.
    /// </summary>
    Task<RouteResult> TryRetryRouteAsync(Guid jobId, CancellationToken cancellationToken = default, string? actorId = null);

    /// <summary>
    /// Reimpresión de algo que ya salió (PrintedConfirmed o PrintedUnknown), con motivo obligatorio
    /// y registrada como evento REPRINT con quién la pidió.
    /// </summary>
    Task<RouteResult> ReprintAsync(Guid jobId, string? reason, string? note, string? actorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enruta todos los trabajos Pending del lote con una sola carga de reglas/impresoras.
    /// </summary>
    Task TryRouteBatchAsync(IReadOnlyList<Guid> jobIds, CancellationToken cancellationToken = default);
}

public record RouteResult(bool Success, int? PrinterId, string? ErrorCode);
