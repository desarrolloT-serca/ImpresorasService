using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Services;

public sealed class WorkerLockCoordinator : IWorkerLockCoordinator
{
    private readonly ImpresorasDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<WorkerLockOptions> _options;
    private readonly ILogger<WorkerLockCoordinator>? _logger;

    public WorkerLockCoordinator(
        ImpresorasDbContext db,
        TimeProvider timeProvider,
        IOptions<WorkerLockOptions> options,
        ILogger<WorkerLockCoordinator>? logger = null)
    {
        _db = db;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> TryAcquireOrRenewAsync(string holder, CancellationToken ct)
    {
        var now = await GetSharedNowAsync(ct);

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

    /// <summary>
    /// Reloj COMUN a todas las instancias, leido de la propia base de datos.
    ///
    /// El umbral del lease (<c>heartbeat_utc &lt;= now - LeaseSeconds</c>) se compara contra una marca
    /// que pudo escribir otra maquina. Con el reloj local, una instancia adelantada 40 s sobre las
    /// demas considera expirado un lease vivo y se lleva el lock: dos titulares, ambos convencidos,
    /// ambos enviando al spooler. Leyendo la hora de donde vive el dato, la deriva entre relojes de
    /// servidor deja de importar.
    ///
    /// Solo contra HANA: en SQLite (tests) se sigue usando el TimeProvider inyectado, que es lo que
    /// permite simular la expiracion del lease de forma determinista.
    /// Si la consulta falla se cae al reloj local con traza: quedarse sin lock por no poder leer la
    /// hora dejaria al Worker inerte, que es peor que el riesgo de deriva.
    /// </summary>
    private async Task<DateTimeOffset> GetSharedNowAsync(CancellationToken ct)
    {
        if (_db.Database.ProviderName?.Contains("Hana", StringComparison.OrdinalIgnoreCase) != true)
            return _timeProvider.GetUtcNow();

        try
        {
            // EF exige que la columna escalar se llame "Value".
            var dbNow = await _db.Database
                .SqlQueryRaw<DateTime>("SELECT CURRENT_UTCTIMESTAMP AS \"Value\" FROM DUMMY")
                .FirstAsync(ct);

            return new DateTimeOffset(DateTime.SpecifyKind(dbNow, DateTimeKind.Utc));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex,
                "No se pudo leer la hora de la base de datos para el lease del lock; se usa el reloj local. " +
                "Con relojes desfasados entre instancias, dos Workers podrian creerse titulares a la vez.");
            return _timeProvider.GetUtcNow();
        }
    }
}
