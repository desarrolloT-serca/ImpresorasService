namespace ImpresorasService.Application.Abstractions;

/// <summary>
/// Lock de instancia única del Worker (G4.1, docs/roadmapimpresoras.md Fase 2.1).
/// </summary>
public interface IWorkerLockCoordinator
{
    /// <summary>
    /// Intenta adquirir o renovar el lock singleton a nombre de <paramref name="holder"/>.
    /// Devuelve true si esta instancia queda (o sigue) siendo la titular.
    /// </summary>
    Task<bool> TryAcquireOrRenewAsync(string holder, CancellationToken ct);
}
