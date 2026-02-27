namespace ImpresorasService.Application.Abstractions;

/// <summary>
/// Resuelve la impresora destino para un trabajo según reglas activas por prioridad.
/// Orden de evaluación: StoreId+DocumentType+Channel > StoreId+DocumentType > StoreId > Global.
/// </summary>
public interface IRoutingResolver
{
    /// <summary>
    /// Resuelve la impresora para el trabajo dado.
    /// </summary>
    /// <param name="storeId">Tienda del trabajo.</param>
    /// <param name="documentType">Tipo de documento.</param>
    /// <param name="channel">Canal (ej. DEFAULT).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>PrinterId de la impresora resuelta, o null si no hay regla aplicable.</returns>
    Task<int?> ResolvePrinterAsync(
        int storeId,
        string documentType,
        string channel,
        CancellationToken cancellationToken = default);
}
