using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ImpresorasService.Api.HealthChecks;

/// <summary>
/// Delata un Worker inerte. El 17/08/2026 el Worker pasó horas sin procesar nada —ingesta,
/// impresión, watchdog IPP, conectividad y alertas de tienda, todos parados— porque no pudo
/// adquirir el lock de instancia única, mientras el servicio figuraba en Running y /health
/// respondía "ok": nada en el sistema reflejaba el problema.
///
/// La Api no puede leer printer_worker_lock (es justo la tabla sobre la que faltan privilegios),
/// así que se usa la huella que el monitor de conectividad deja en cada impresora: si el Worker
/// vive, TODAS las impresoras activas se refrescan cada PrinterConnectivity:IntervalSeconds (30s
/// por defecto). La Api también sondea bajo demanda desde la pantalla de impresoras, pero solo las
/// de la tienda que se abre — por eso se mira la MENOS reciente, que es la que delata al Worker.
///
/// Reporta Degraded y no Unhealthy a propósito: la Api está perfectamente sana, y /health no debe
/// devolver 503 (rompería a cualquiera que lo use como señal de tráfico) por un fallo del Worker.
/// </summary>
public sealed class WorkerActivityHealthCheck : IHealthCheck
{
    public const string Name = "worker";

    private readonly ImpresorasDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _staleAfter;

    public WorkerActivityHealthCheck(
        ImpresorasDbContext dbContext, TimeProvider timeProvider, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        var minutes = configuration.GetValue<int?>("Worker:ActivityStaleMinutes") ?? 5;
        _staleAfter = TimeSpan.FromMinutes(Math.Max(1, minutes));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var activeOnly = true;

        // MIN ignora los NULL en SQL: una impresora recién dada de alta y aún no sondeada no
        // dispara el check (evita un falso positivo durante su primer ciclo).
        var oldestCheck = await _dbContext.Printers
            .AsNoTracking()
            .Where(p => p.IsActive == activeOnly)
            .MinAsync(p => (DateTimeOffset?)p.LastConnectionCheckAtUtc, cancellationToken);

        if (oldestCheck is null)
            return HealthCheckResult.Healthy("Sin impresoras activas sondeadas todavia; nada que vigilar.");

        var age = _timeProvider.GetUtcNow() - oldestCheck.Value;
        if (age <= _staleAfter)
            return HealthCheckResult.Healthy($"Worker activo; ultimo sondeo de conectividad hace {(int)age.TotalSeconds}s.");

        return HealthCheckResult.Degraded(
            $"El Worker no sondea la conectividad desde hace {(int)age.TotalMinutes} min. " +
            "Probablemente no tiene el lock de instancia unica: ingesta, impresion y alertas estarian parados.");
    }
}
