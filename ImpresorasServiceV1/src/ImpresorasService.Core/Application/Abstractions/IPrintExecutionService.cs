namespace ImpresorasService.Application.Abstractions;

/// <summary>
/// Servicio que procesa jobs en estado Routed/RetryScheduled y los envía al spooler.
/// </summary>
public interface IPrintExecutionService
{
    /// <summary>
    /// Ejecuta un lote de jobs elegibles. Retorna el número procesado.
    /// </summary>
    Task<int> ExecuteBatchAsync(int batchSize, CancellationToken cancellationToken = default);
}
