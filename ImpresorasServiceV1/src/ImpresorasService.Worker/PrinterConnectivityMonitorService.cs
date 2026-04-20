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

        var updates = new List<ConnectivityUpdate>(printers.Count);

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
                updates.Add(new ConnectivityUpdate(
                    p.PrinterId,
                    LastOk: false,
                    FailuresStreak: 0,
                    CheckedAtUtc: now,
                    Transport: null,
                    Error: "HOST_NOT_CONFIGURED"));
                continue;
            }

            var result = await TryConnectAnyPortAsync(host!, ct);
            if (result.Ok)
            {
                updates.Add(new ConnectivityUpdate(
                    p.PrinterId,
                    LastOk: true,
                    FailuresStreak: 0,
                    CheckedAtUtc: now,
                    Transport: result.Transport,
                    Error: null));
            }
            else
            {
                var nextStreak = Math.Clamp(p.ConnectionFailuresStreak + 1, 0, 999);
                updates.Add(new ConnectivityUpdate(
                    p.PrinterId,
                    LastOk: false,
                    FailuresStreak: nextStreak,
                    CheckedAtUtc: now,
                    Transport: null,
                    Error: result.Error ?? "NO_CONNECTION"));
            }
        }

        foreach (var update in updates)
            ApplyConnectivityUpdate(db, update);

        await db.SaveChangesAsync(ct);
    }

    private static void ApplyConnectivityUpdate(
        ImpresorasDbContext db,
        ConnectivityUpdate update)
    {
        var entity = new ImpresorasService.Domain.Entities.Printer
        {
            PrinterId = update.PrinterId
        };

        db.Attach(entity);
        entity.LastConnectionOk = update.LastOk;
        entity.ConnectionFailuresStreak = update.FailuresStreak;
        entity.LastConnectionCheckAtUtc = update.CheckedAtUtc;
        entity.LastConnectionTransport = update.Transport;
        entity.LastConnectionError = update.Error;

        db.Entry(entity).Property(x => x.LastConnectionOk).IsModified = true;
        db.Entry(entity).Property(x => x.ConnectionFailuresStreak).IsModified = true;
        db.Entry(entity).Property(x => x.LastConnectionCheckAtUtc).IsModified = true;
        db.Entry(entity).Property(x => x.LastConnectionTransport).IsModified = true;
        db.Entry(entity).Property(x => x.LastConnectionError).IsModified = true;
    }

    private sealed record ConnectivityUpdate(
        int PrinterId,
        bool LastOk,
        int FailuresStreak,
        DateTimeOffset CheckedAtUtc,
        string? Transport,
        string? Error);

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

