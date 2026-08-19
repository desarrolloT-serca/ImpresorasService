using ImpresorasService.Api.HealthChecks;
using ImpresorasService.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// Regresión del 17/08/2026: el Worker perdió el lock de instancia única y quedó inerte durante
/// horas —ingesta, impresión, conectividad y alertas parados— mientras el servicio figuraba en
/// Running y /health respondía "ok". Nada en el sistema lo delataba.
/// </summary>
public sealed class WorkerActivityHealthCheckTests
{
    [Fact]
    public async Task Healthy_WhenAllActivePrintersProbedRecently()
    {
        var result = await RunCheckAsync(
            (isActive: true, ageFromNow: TimeSpan.FromSeconds(-30)),
            (isActive: true, ageFromNow: TimeSpan.FromSeconds(-45)));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// <summary>
    /// El caso real: la Api sondea bajo demanda las impresoras de la tienda que se abre en pantalla,
    /// así que algunas se ven frescas aunque el Worker lleve horas parado. Por eso el check mira la
    /// MENOS reciente; si mirase la más reciente, no habría detectado nada aquel día.
    /// </summary>
    [Fact]
    public async Task Degraded_WhenSomePrinterIsStale_EvenIfAnotherWasJustProbedByTheApi()
    {
        var result = await RunCheckAsync(
            (isActive: true, ageFromNow: TimeSpan.FromSeconds(-10)),
            (isActive: true, ageFromNow: TimeSpan.FromHours(-3)));

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("no sondea", result.Description);
    }

    [Fact]
    public async Task IgnoresInactivePrinters()
    {
        var result = await RunCheckAsync(
            (isActive: true, ageFromNow: TimeSpan.FromSeconds(-20)),
            (isActive: false, ageFromNow: TimeSpan.FromDays(-9)));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Healthy_WhenNoPrinterHasEverBeenProbed()
    {
        // Impresora recién dada de alta: aún no sondeada, no debe disparar el check.
        var result = await RunCheckAsync((isActive: true, ageFromNow: (TimeSpan?)null));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// <summary>
    /// El check no tiene mas señal que la huella del monitor de conectividad: con el monitor
    /// apagado esa huella no se refresca nunca y, sin esta salida, acusaba al lock de un Worker
    /// sano para siempre.
    /// </summary>
    [Fact]
    public async Task Healthy_WhenConnectivityMonitorIsDisabled()
    {
        var result = await RunCheckAsync(
            connectivityMonitorEnabled: false,
            printers: (isActive: true, ageFromNow: TimeSpan.FromHours(-3)));

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("PrinterConnectivity:Enabled=false", result.Description);
    }

    private static async Task<HealthCheckResult> RunCheckAsync(
        params (bool isActive, TimeSpan? ageFromNow)[] printers)
        => await RunCheckAsync(true, printers);

    private static async Task<HealthCheckResult> RunCheckAsync(
        bool connectivityMonitorEnabled,
        params (bool isActive, TimeSpan? ageFromNow)[] printers)
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;
        var now = DateTimeOffset.UtcNow;

        var printerId = 1;
        foreach (var (isActive, ageFromNow) in printers)
        {
            db.Printers.Add(new Printer
            {
                PrinterId = printerId,
                StoreId = 1,
                PrinterName = $"P{printerId}",
                SpoolQueue = $@"\\host\q{printerId}",
                Host = "host",
                IsActive = isActive,
                LastConnectionCheckAtUtc = ageFromNow.HasValue ? now.Add(ageFromNow.Value) : null,
                CreatedAtUtc = now.AddDays(-1),
                UpdatedAtUtc = now.AddDays(-1)
            });
            printerId++;
        }

        await db.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PrinterConnectivity:Enabled"] = connectivityMonitorEnabled ? "true" : "false"
            })
            .Build();

        var check = new WorkerActivityHealthCheck(db, TimeProvider.System, configuration);

        return await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
    }
}
