using System.Diagnostics;
using System.Net.Sockets;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ImpresorasService.Worker;

public sealed class PrinterConnectivityMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PrinterConnectivityMonitorService> _logger;

    // Ajustable: frecuencia de chequeo.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    // Puertos comunes de impresión / compartición.
    private static readonly int[] PortsToTry = [515, 9100, 631, 445, 139];

    public PrinterConnectivityMonitorService(
        IServiceScopeFactory scopeFactory,
        ILogger<PrinterConnectivityMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pequeño delay inicial para dejar que el host arranque.
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallo en monitor de conectividad de impresoras.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

        var printers = await db.Printers
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new
            {
                p.PrinterId,
                p.SpoolQueue,
                p.Host,
                p.ConnectionFailuresStreak
            })
            .ToListAsync(ct);

        if (printers.Count == 0) return;

        foreach (var p in printers)
        {
            ct.ThrowIfCancellationRequested();

            var host = !string.IsNullOrWhiteSpace(p.Host)
                ? ExtractHostFromMaybeUnc(p.Host!)
                : ExtractHostFromSpoolQueue(p.SpoolQueue);

            var now = DateTimeOffset.UtcNow;

            // Host no configurado: warning en dashboard, pero no incrementamos streak de "caída".
            if (string.IsNullOrWhiteSpace(host))
            {
                await UpdatePrinterConnectivityAsync(
                    db,
                    p.PrinterId,
                    lastOk: false,
                    failuresStreak: 0,
                    checkedAtUtc: now,
                    transport: null,
                    error: "HOST_NOT_CONFIGURED",
                    ct);
                continue;
            }

            var result = await TryConnectAnyPortAsync(host!, ct);
            if (result.Ok)
            {
                await UpdatePrinterConnectivityAsync(
                    db,
                    p.PrinterId,
                    lastOk: true,
                    failuresStreak: 0,
                    checkedAtUtc: now,
                    transport: result.Transport,
                    error: null,
                    ct);
            }
            else
            {
                var nextStreak = Math.Clamp(p.ConnectionFailuresStreak + 1, 0, 999);
                await UpdatePrinterConnectivityAsync(
                    db,
                    p.PrinterId,
                    lastOk: false,
                    failuresStreak: nextStreak,
                    checkedAtUtc: now,
                    transport: null,
                    error: result.Error ?? "NO_CONNECTION",
                    ct);
            }
        }
    }

    private static async Task UpdatePrinterConnectivityAsync(
        ImpresorasDbContext db,
        int printerId,
        bool lastOk,
        int failuresStreak,
        DateTimeOffset checkedAtUtc,
        string? transport,
        string? error,
        CancellationToken ct)
    {
        var entity = new ImpresorasService.Domain.Entities.Printer
        {
            PrinterId = printerId
        };

        db.Attach(entity);
        entity.LastConnectionOk = lastOk;
        entity.ConnectionFailuresStreak = failuresStreak;
        entity.LastConnectionCheckAtUtc = checkedAtUtc;
        entity.LastConnectionTransport = transport;
        entity.LastConnectionError = error;

        db.Entry(entity).Property(x => x.LastConnectionOk).IsModified = true;
        db.Entry(entity).Property(x => x.ConnectionFailuresStreak).IsModified = true;
        db.Entry(entity).Property(x => x.LastConnectionCheckAtUtc).IsModified = true;
        db.Entry(entity).Property(x => x.LastConnectionTransport).IsModified = true;
        db.Entry(entity).Property(x => x.LastConnectionError).IsModified = true;

        await db.SaveChangesAsync(ct);
    }

    private static async Task<(bool Ok, string? Transport, string? Error)> TryConnectAnyPortAsync(string host, CancellationToken ct)
    {
        foreach (var port in PortsToTry)
        {
            var sw = Stopwatch.StartNew();
            using var tcp = new TcpClient();

            const int timeoutMs = 1500;
            var connectTask = tcp.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, ct));

            if (!ReferenceEquals(completed, connectTask))
            {
                _ = connectTask.ContinueWith(
                    t => { _ = t.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                continue;
            }

            try
            {
                await connectTask;
                return (true, $"tcp/{port}", null);
            }
            catch (Exception ex)
            {
                // Intentar siguiente puerto.
                _ = sw; // mantiene simetría por si luego se usa.
                _ = ex;
            }
        }

        return (false, null, $"No se pudo conectar a {host} (puertos TCP: {string.Join(", ", PortsToTry)})");
    }

    private static string? ExtractHostFromMaybeUnc(string hostOrUnc)
    {
        var trimmed = hostOrUnc.Trim();
        if (trimmed.StartsWith("\\\\", StringComparison.Ordinal))
        {
            trimmed = trimmed.TrimStart('\\');
            var parts = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 1 ? parts[0].Trim() : null;
        }
        if (trimmed.Contains('\\'))
        {
            var parts = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 1 ? parts[0].Trim() : null;
        }
        return trimmed;
    }

    private static string? ExtractHostFromSpoolQueue(string spoolQueue)
    {
        if (string.IsNullOrWhiteSpace(spoolQueue)) return null;
        if (spoolQueue.Length < 2 || spoolQueue[0] != '\\' || spoolQueue[1] != '\\') return null;
        var parts = spoolQueue.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 1 ? parts[0].Trim() : null;
    }
}

