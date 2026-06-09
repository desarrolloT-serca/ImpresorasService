using System.Net.Sockets;
using ImpresorasService.Domain.Connectivity;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ImpresorasService.Worker;

public sealed class PrinterConnectivityMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PrinterConnectivityMonitorService> _logger;
    private readonly PrinterConnectivityOptions _options;

    public PrinterConnectivityMonitorService(
        IServiceScopeFactory scopeFactory,
        ILogger<PrinterConnectivityMonitorService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = PrinterConnectivityOptions.FromConfiguration(configuration);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Monitor de conectividad de impresoras desactivado por configuracion.");
            return;
        }

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

            await Task.Delay(_options.Interval, stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

        var printers = await db.Printers
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new PrinterConnectivityCandidate(
                p.PrinterId,
                p.SpoolQueue,
                p.Host,
                p.ConnectionFailuresStreak))
            .ToListAsync(ct);

        if (printers.Count == 0)
            return;

        var updates = _options.MaxParallelChecks <= 1
            ? await BuildUpdatesSequentiallyAsync(printers, ct)
            : await BuildUpdatesInParallelAsync(printers, ct);

        foreach (var update in updates)
            ApplyConnectivityUpdate(db, update);

        await db.SaveChangesAsync(ct);
    }

    private async Task<List<ConnectivityUpdate>> BuildUpdatesSequentiallyAsync(
        IReadOnlyCollection<PrinterConnectivityCandidate> printers,
        CancellationToken ct)
    {
        var updates = new List<ConnectivityUpdate>(printers.Count);
        foreach (var printer in printers)
        {
            ct.ThrowIfCancellationRequested();
            updates.Add(await BuildUpdateAsync(printer, ct));
        }

        return updates;
    }

    private async Task<List<ConnectivityUpdate>> BuildUpdatesInParallelAsync(
        IReadOnlyCollection<PrinterConnectivityCandidate> printers,
        CancellationToken ct)
    {
        using var semaphore = new SemaphoreSlim(_options.MaxParallelChecks);
        var tasks = printers.Select(async printer =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await BuildUpdateAsync(printer, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        return (await Task.WhenAll(tasks)).ToList();
    }

    private async Task<ConnectivityUpdate> BuildUpdateAsync(
        PrinterConnectivityCandidate candidate,
        CancellationToken ct)
    {
        var printer = new Printer
        {
            PrinterId = candidate.PrinterId,
            ConnectionFailuresStreak = candidate.ConnectionFailuresStreak
        };

        var host = !string.IsNullOrWhiteSpace(candidate.Host)
            ? ExtractHostFromMaybeUnc(candidate.Host!)
            : ExtractHostFromSpoolQueue(candidate.SpoolQueue);

        if (string.IsNullOrWhiteSpace(host))
        {
            PrinterConnectivityState.ApplyProbeResult(
                printer,
                reachable: false,
                transport: null,
                error: PrinterConnectivityState.HostNotConfiguredError,
                checkedAtUtc: DateTimeOffset.UtcNow);

            return ToUpdate(printer);
        }

        var result = await TryConnectAnyPortAsync(host!, ct);
        PrinterConnectivityState.ApplyProbeResult(
            printer,
            result.Ok,
            result.Transport,
            result.Error,
            DateTimeOffset.UtcNow);

        return ToUpdate(printer);
    }

    private static void ApplyConnectivityUpdate(
        ImpresorasDbContext db,
        ConnectivityUpdate update)
    {
        var entity = new Printer
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

    private static ConnectivityUpdate ToUpdate(Printer printer)
        => new(
            printer.PrinterId,
            printer.LastConnectionOk == true,
            printer.ConnectionFailuresStreak,
            printer.LastConnectionCheckAtUtc ?? DateTimeOffset.UtcNow,
            printer.LastConnectionTransport,
            printer.LastConnectionError);

    private async Task<(bool Ok, string? Transport, string? Error)> TryConnectAnyPortAsync(
        string host,
        CancellationToken ct)
    {
        foreach (var port in _options.Ports)
        {
            using var tcp = new TcpClient();
            var connectTask = tcp.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(_options.TimeoutMsPerPort, ct));
            ct.ThrowIfCancellationRequested();

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
            catch
            {
                // Intentar siguiente puerto.
            }
        }

        return (false, null, PrinterConnectivityState.NoConnectionError);
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
        if (string.IsNullOrWhiteSpace(spoolQueue))
            return null;
        if (spoolQueue.Length < 2 || spoolQueue[0] != '\\' || spoolQueue[1] != '\\')
            return null;

        var parts = spoolQueue.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 1 ? parts[0].Trim() : null;
    }

    private sealed record PrinterConnectivityCandidate(
        int PrinterId,
        string SpoolQueue,
        string? Host,
        int ConnectionFailuresStreak);

    private sealed record ConnectivityUpdate(
        int PrinterId,
        bool LastOk,
        int FailuresStreak,
        DateTimeOffset CheckedAtUtc,
        string? Transport,
        string? Error);

    private sealed class PrinterConnectivityOptions
    {
        private static readonly int[] DefaultPorts = [515, 9100, 631, 445, 139];

        public bool Enabled { get; private init; } = true;
        public int IntervalSeconds { get; private init; } = 30;
        public int TimeoutMsPerPort { get; private init; } = 1500;
        public int MaxParallelChecks { get; private init; } = 1;
        public int[] Ports { get; private init; } = DefaultPorts;
        public TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(1, IntervalSeconds));

        public static PrinterConnectivityOptions FromConfiguration(IConfiguration configuration)
        {
            var section = configuration.GetSection("PrinterConnectivity");
            return new PrinterConnectivityOptions
            {
                Enabled = ReadBool(section["Enabled"], true),
                IntervalSeconds = Math.Max(1, ReadInt(section["IntervalSeconds"], 30)),
                TimeoutMsPerPort = Math.Max(100, ReadInt(section["TimeoutMsPerPort"], 1500)),
                MaxParallelChecks = Math.Max(1, ReadInt(section["MaxParallelChecks"], 1)),
                Ports = ReadPorts(section) is { Length: > 0 } ports ? ports : DefaultPorts
            };
        }

        private static bool ReadBool(string? value, bool fallback)
            => bool.TryParse(value, out var parsed) ? parsed : fallback;

        private static int ReadInt(string? value, int fallback)
            => int.TryParse(value, out var parsed) ? parsed : fallback;

        private static int[] ReadPorts(IConfigurationSection section)
            => section.GetSection("Ports")
                .GetChildren()
                .Select(item => ReadInt(item.Value, 0))
                .Where(port => port > 0 && port <= 65535)
                .Distinct()
                .ToArray();
    }
}
