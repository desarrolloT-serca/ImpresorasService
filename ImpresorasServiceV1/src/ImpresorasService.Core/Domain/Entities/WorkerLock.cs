namespace ImpresorasService.Domain.Entities;

/// <summary>
/// Fila singleton (Id=1) usada como lock de instancia única del Worker (G4.1,
/// docs/roadmapimpresoras.md Fase 2.1). El titular se identifica por <see cref="Holder"/>
/// y renueva <see cref="HeartbeatAtUtc"/> periódicamente; si deja de renovar, el lease expira
/// y otra instancia puede tomar el lock.
/// </summary>
public class WorkerLock
{
    public int Id { get; set; }

    public string? Holder { get; set; }

    public DateTimeOffset HeartbeatAtUtc { get; set; }
}
