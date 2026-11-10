using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Worker;

/// <summary>
/// A las 00:00 (zona de negocio) guarda el contador de páginas de cada impresora: las páginas del día
/// anterior son esa lectura menos la suya (ERP_SERCA docs/73, opción E). La que no responde se reintenta
/// al momento y luego cada <c>PageCounter:RetryMinutes</c> hasta que responda o empiece otro día. Si el
/// Worker estaba parado a medianoche, lee al arrancar: esa lectura lleva su hora (<c>read_at_utc</c>).
/// Qué se lee y cómo se cuenta está en <see cref="ContadorPaginas"/>; aquí solo el hosting.
/// </summary>
public sealed class PageCounterBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILectorContador _lector;
    private readonly IOptions<PageCounterOptions> _options;
    private readonly ILogger<PageCounterBackgroundService> _logger;
    private readonly TimeProvider _reloj;
    private readonly WorkerLockState _lockState;
    private readonly TimeZoneInfo _zona;

    public PageCounterBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILectorContador lector,
        IOptions<PageCounterOptions> options,
        ILogger<PageCounterBackgroundService> logger,
        TimeProvider reloj,
        WorkerLockState lockState,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _lector = lector;
        _options = options;
        _logger = logger;
        _reloj = reloj;
        _lockState = lockState;
        _zona = BusinessTimeZoneClock.Resolve(configuration["Dashboard:BusinessTimeZone"], logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Contador de páginas desactivado (PageCounter:Enabled=false).");
            return;
        }

        var reintento = TimeSpan.FromMinutes(Math.Max(1, _options.Value.RetryMinutes));
        // Un aviso por impresora y día, no uno por intento.
        var avisadas = new HashSet<int>();
        DateOnly? diaAvisos = null;

        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Sin el lock de instancia única esta réplica no escribe (mismo criterio que el resto).
            if (!_lockState.IsHolder)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_reloj.GetUtcNow(), _zona).Date);
            var faltan = true;
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
                var sinLectura = await ContadorPaginas.TomarAsync(db, _lector, hoy, _reloj, stoppingToken);
                faltan = sinLectura.Count > 0;

                if (diaAvisos != hoy)
                {
                    diaAvisos = hoy;
                    avisadas.Clear();
                }
                foreach (var p in sinLectura.Where(p => avisadas.Add(p.PrinterId)))
                    _logger.LogWarning(
                        "Sin contador de páginas de {Impresora} ({Host}) para el {Dia:dd/MM/yyyy}: se reintenta cada {Minutos} min.",
                        p.PrinterName, p.Host, hoy, reintento.TotalMinutes);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (FallosBd.EsFalloDeConexion(ex))
            {
                // La base no responde: lo anota EstadoBd, una vez y con el motivo.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallo al guardar el contador de páginas del {Dia:dd/MM/yyyy}.", hoy);
            }

            // Hasta las 00:00 del día siguiente o, si falta alguna, hasta el próximo intento.
            var medianoche = BusinessTimeZoneClock.DayStartUtc(hoy.AddDays(1), _zona) - _reloj.GetUtcNow();
            var espera = faltan && reintento < medianoche ? reintento : medianoche;
            await Task.Delay(espera > TimeSpan.FromSeconds(1) ? espera : TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
