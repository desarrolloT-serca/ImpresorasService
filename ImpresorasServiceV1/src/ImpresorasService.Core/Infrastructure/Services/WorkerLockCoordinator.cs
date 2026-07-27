using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Services;

public sealed class WorkerLockCoordinator : IWorkerLockCoordinator
{
    private readonly ImpresorasDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<WorkerLockOptions> _options;

    public WorkerLockCoordinator(ImpresorasDbContext db, TimeProvider timeProvider, IOptions<WorkerLockOptions> options)
    {
        _db = db;
        _timeProvider = timeProvider;
        _options = options;
    }

    public async Task<bool> TryAcquireOrRenewAsync(string holder, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();

        var exists = await _db.WorkerLocks.AsNoTracking().AnyAsync(x => x.Id == 1, ct);
        if (!exists)
        {
            _db.WorkerLocks.Add(new WorkerLock { Id = 1, Holder = null, HeartbeatAtUtc = DateTimeOffset.MinValue });
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Otra instancia ganó la carrera de siembra inicial de la fila singleton;
                // seguir al UPDATE condicional de abajo.
                _db.ChangeTracker.Clear();
            }
        }

        var staleThreshold = now - TimeSpan.FromSeconds(_options.Value.LeaseSeconds);

        // UPDATE condicional (mismo patrón que SapHanaJobSourceAdapter.RenewJobLeasesAsync):
        // solo afecta la fila si nadie más la posee o si el lease expiró. rowcount=1 => lock obtenido/renovado.
        var rows = await _db.WorkerLocks
            .Where(x => x.Id == 1 && (x.Holder == holder || x.HeartbeatAtUtc <= staleThreshold))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Holder, holder)
                .SetProperty(x => x.HeartbeatAtUtc, now), ct);

        return rows == 1;
    }
}
