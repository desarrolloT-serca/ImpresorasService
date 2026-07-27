using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// G4.1 (docs/roadmapimpresoras.md Fase 2.1): lock de instancia única del Worker.
/// Acceptance del roadmap: arrancar 2 Workers → solo uno procesa; matar al titular → el segundo
/// toma el relevo tras expirar el lease. Aquí se prueba el coordinador (sin levantar hosts reales)
/// contra SQLite en memoria, mismo patrón que <see cref="Persistence.TelegramConfigCompatibilityTests"/>.
/// </summary>
public sealed class WorkerLockCoordinatorTests
{
    private static WorkerLockCoordinator CreateCoordinator(
        ImpresorasService.Infrastructure.Persistence.ImpresorasDbContext db,
        ManualTimeProvider clock,
        int leaseSeconds = 30)
        => new(db, clock, Options.Create(new WorkerLockOptions { LeaseSeconds = leaseSeconds }));

    [Fact]
    public async Task TryAcquireOrRenewAsync_NoRowYet_SeedsAndAcquires()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var clock = new ManualTimeProvider();
        clock.SetUtcNow(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));
        var coordinator = CreateCoordinator(setup.Db, clock);

        var acquired = await coordinator.TryAcquireOrRenewAsync("worker-A", CancellationToken.None);

        Assert.True(acquired);
    }

    [Fact]
    public async Task TryAcquireOrRenewAsync_SecondHolder_BlockedWhileLeaseActive()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var clock = new ManualTimeProvider();
        clock.SetUtcNow(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));
        var coordinator = CreateCoordinator(setup.Db, clock, leaseSeconds: 30);

        Assert.True(await coordinator.TryAcquireOrRenewAsync("worker-A", CancellationToken.None));

        clock.SetUtcNow(clock.GetUtcNow().AddSeconds(10));
        var second = await coordinator.TryAcquireOrRenewAsync("worker-B", CancellationToken.None);

        Assert.False(second);
    }

    [Fact]
    public async Task TryAcquireOrRenewAsync_SameHolder_RenewsWithoutLosingLock()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var clock = new ManualTimeProvider();
        clock.SetUtcNow(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));
        var coordinator = CreateCoordinator(setup.Db, clock, leaseSeconds: 30);

        Assert.True(await coordinator.TryAcquireOrRenewAsync("worker-A", CancellationToken.None));

        // Pasa más que el lease, pero el mismo holder renueva antes de que otro lo reclame.
        clock.SetUtcNow(clock.GetUtcNow().AddSeconds(20));
        Assert.True(await coordinator.TryAcquireOrRenewAsync("worker-A", CancellationToken.None));
    }

    [Fact]
    public async Task TryAcquireOrRenewAsync_LeaseExpired_SecondHolderTakesOver()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var clock = new ManualTimeProvider();
        clock.SetUtcNow(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));
        var coordinator = CreateCoordinator(setup.Db, clock, leaseSeconds: 30);

        Assert.True(await coordinator.TryAcquireOrRenewAsync("worker-A", CancellationToken.None));

        // worker-A "muere" (deja de renovar); pasa el lease completo.
        clock.SetUtcNow(clock.GetUtcNow().AddSeconds(31));
        var tookOver = await coordinator.TryAcquireOrRenewAsync("worker-B", CancellationToken.None);

        Assert.True(tookOver);
    }
}
