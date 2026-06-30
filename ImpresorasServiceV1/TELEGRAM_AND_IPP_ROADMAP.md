# Roadmap de implementación post-migración de BD

Este documento describe, en orden, todos los pasos de código necesarios una vez que el administrador de base de datos haya ejecutado los scripts DDL solicitados. Cubre dos funcionalidades independientes que comparten la misma ventana de cambios en BD:

1. **Detección de compatibilidad IPP por impresora** (`ipp_supported`)
2. **Sistema de alertas críticas por Telegram**

---

## Índice

- [Estado actual tras la migración](#estado-actual-tras-la-migración)
- [Bloque 1 — Compatibilidad IPP](#bloque-1--compatibilidad-ipp)
  - [1.1 Entidad `Printer`](#11-entidad-printer)
  - [1.2 Mapeo en `ImpresorasDbContext`](#12-mapeo-en-impresorasdbcontext)
  - [1.3 Monitor de conectividad](#13-monitor-de-conectividad)
  - [1.4 Watchdog de SpoolAccepted](#14-watchdog-de-spoolaccepted)
  - [1.5 API — exponer el campo](#15-api--exponer-el-campo)
  - [1.6 UI — mostrar compatibilidad en listado de impresoras](#16-ui--mostrar-compatibilidad-en-listado-de-impresoras)
- [Bloque 2 — Sistema de alertas Telegram](#bloque-2--sistema-de-alertas-telegram)
  - [2.1 Crear el bot en Telegram](#21-crear-el-bot-en-telegram)
  - [2.2 Obtener el chat_id del destinatario](#22-obtener-el-chat_id-del-destinatario)
  - [2.3 Opciones de configuración (`TelegramOptions`)](#23-opciones-de-configuración-telegramoptions)
  - [2.4 Interfaz `ITelegramNotifier`](#24-interfaz-itelegramnotifier)
  - [2.5 Implementación `TelegramNotifierService`](#25-implementación-telegramnotifierservice)
  - [2.6 Extraer `ComputeHealth` a Core](#26-extraer-computehealth-a-core)
  - [2.7 Servicio `StoreHealthAlertBackgroundService`](#27-servicio-storehealthalertbackgroundservice)
  - [2.8 Registro en DI y `Program.cs`](#28-registro-en-di-y-programcs)
  - [2.9 UI — gestión de chats y configuración](#29-ui--gestión-de-chats-y-configuración)
  - [2.10 API — endpoints de gestión Telegram](#210-api--endpoints-de-gestión-telegram)
- [Configuración final de `appsettings.json`](#configuración-final-de-appsetingsjson)
- [Plan de pruebas](#plan-de-pruebas)
- [Despliegue](#despliegue)

---

## Estado actual tras la migración

Una vez ejecutados los scripts DDL, la BD quedará con estas tablas nuevas/modificadas:

| Objeto | Tipo | Estado |
|---|---|---|
| `printer_printer.ipp_supported` | Columna TINYINT nullable | Nueva |
| `printer_telegram_config` | Tabla | Nueva (con 1 fila inicial) |
| `printer_telegram_chat` | Tabla | Nueva (vacía) |
| `printer_alert_state` | Tabla | Nueva (vacía) |

El código ya tiene las entidades EF (`TelegramConfig`, `TelegramChat`, `StoreAlertState`) y su mapeo en `ImpresorasDbContext` listos. Lo que queda es la lógica de negocio.

---

## Bloque 1 — Compatibilidad IPP

### 1.1 Entidad `Printer`

**Archivo:** `src/ImpresorasService.Core/Domain/Entities/Printer.cs`

Añadir la propiedad al final de la clase:

```csharp
/// <summary>
/// Indica si la impresora respondió correctamente a un sondeo IPP real.
/// null = no comprobado todavía, true = soporta IPP, false = no soporta IPP o sin respuesta.
/// </summary>
public bool? IppSupported { get; set; }
```

---

### 1.2 Mapeo en `ImpresorasDbContext`

**Archivo:** `src/ImpresorasService.Core/Infrastructure/Persistence/ImpresorasDbContext.cs`

Dentro del bloque `modelBuilder.Entity<Printer>(entity => { ... })`, añadir al final:

```csharp
entity.Property(x => x.IppSupported).HasColumnName("ipp_supported");
```

---

### 1.3 Monitor de conectividad

**Archivo:** `src/ImpresorasService.Worker/PrinterConnectivityMonitorService.cs`

Este es el cambio más relevante del Bloque 1. El monitor ya sondea el puerto 631. Si ese puerto responde, se lanza un sondeo IPP real reutilizando `IIppConfirmationService`.

#### Cambios necesarios:

**a) Inyectar `IIppConfirmationService` en el constructor:**

```csharp
private readonly IIppConfirmationService _ippService;

public PrinterConnectivityMonitorService(
    IServiceScopeFactory scopeFactory,
    ILogger<PrinterConnectivityMonitorService> logger,
    IConfiguration configuration,
    IIppConfirmationService ippService)          // <-- nuevo
{
    _scopeFactory = scopeFactory;
    _logger = logger;
    _options = PrinterConnectivityOptions.FromConfiguration(configuration);
    _ippService = ippService;                    // <-- nuevo
}
```

**b) Ampliar `PrinterConnectivityCandidate` para incluir `IppSupported`:**

```csharp
private sealed record PrinterConnectivityCandidate(
    int PrinterId,
    string SpoolQueue,
    string? Host,
    int ConnectionFailuresStreak,
    bool? IppSupported);   // <-- nuevo
```

**c) Ampliar la proyección en `RunOnceAsync`:**

```csharp
var printers = await db.Printers
    .AsNoTracking()
    .Where(p => p.IsActive == activeOnly)
    .Select(p => new PrinterConnectivityCandidate(
        p.PrinterId,
        p.SpoolQueue,
        p.Host,
        p.ConnectionFailuresStreak,
        p.IppSupported))   // <-- nuevo
    .ToListAsync(ct);
```

**d) Ampliar `ConnectivityUpdate` para incluir `IppSupported`:**

```csharp
private sealed record ConnectivityUpdate(
    int PrinterId,
    bool LastOk,
    int FailuresStreak,
    DateTimeOffset CheckedAtUtc,
    string? Transport,
    string? Error,
    bool? IppSupported);   // <-- nuevo
```

**e) En `BuildUpdateAsync`, después de obtener el resultado de conectividad, sondear IPP si el puerto 631 respondió:**

```csharp
private async Task<ConnectivityUpdate> BuildUpdateAsync(
    PrinterConnectivityCandidate candidate,
    CancellationToken ct)
{
    // ... lógica existente de host y TryConnectAnyPortAsync ...

    var result = await TryConnectAnyPortAsync(host!, ct);

    // Sondeo IPP: solo si el puerto 631 fue el que respondió
    bool? ippSupported = candidate.IppSupported; // mantener valor previo por defecto
    if (result.Ok && result.Transport == "tcp/631" && !string.IsNullOrWhiteSpace(host))
    {
        var ippResult = await _ippService.QueryPrinterStateAsync(host!, ct);
        ippSupported = ippResult.Outcome != IppOutcome.Unavailable;
        _logger.LogInformation(
            "IPP probe {Host}: {Result}",
            host, ippSupported.Value ? "compatible" : "no compatible");
    }
    else if (result.Ok && result.Transport != "tcp/631")
    {
        // Conecta por otro puerto: IPP definitivamente no disponible en este ciclo.
        // No sobreescribimos si ya había un true previo (podría ser un fallo puntual del 631).
        // Solo marcamos false si nunca se había comprobado.
        if (candidate.IppSupported is null)
            ippSupported = false;
    }

    PrinterConnectivityState.ApplyProbeResult(
        printer, result.Ok, result.Transport, result.Error, DateTimeOffset.UtcNow);

    return ToUpdate(printer, ippSupported);
}
```

**f) Actualizar `ToUpdate` para propagar `IppSupported`:**

```csharp
private static ConnectivityUpdate ToUpdate(Printer printer, bool? ippSupported)
    => new(
        printer.PrinterId,
        printer.LastConnectionOk == true,
        printer.ConnectionFailuresStreak,
        printer.LastConnectionCheckAtUtc ?? DateTimeOffset.UtcNow,
        printer.LastConnectionTransport,
        printer.LastConnectionError,
        ippSupported);
```

**g) En `ApplyConnectivityUpdate`, persistir el nuevo campo:**

```csharp
private static void ApplyConnectivityUpdate(ImpresorasDbContext db, ConnectivityUpdate update)
{
    var entity = new Printer { PrinterId = update.PrinterId };
    db.Attach(entity);

    // ... propiedades existentes ...
    entity.LastConnectionOk = update.LastOk;
    entity.ConnectionFailuresStreak = update.FailuresStreak;
    entity.LastConnectionCheckAtUtc = update.CheckedAtUtc;
    entity.LastConnectionTransport = update.Transport;
    entity.LastConnectionError = update.Error;

    // Nuevo
    if (update.IppSupported.HasValue)
    {
        entity.IppSupported = update.IppSupported;
        db.Entry(entity).Property(x => x.IppSupported).IsModified = true;
    }

    db.Entry(entity).Property(x => x.LastConnectionOk).IsModified = true;
    db.Entry(entity).Property(x => x.ConnectionFailuresStreak).IsModified = true;
    db.Entry(entity).Property(x => x.LastConnectionCheckAtUtc).IsModified = true;
    db.Entry(entity).Property(x => x.LastConnectionTransport).IsModified = true;
    db.Entry(entity).Property(x => x.LastConnectionError).IsModified = true;
}
```

---

### 1.4 Watchdog de SpoolAccepted

**Archivo:** `src/ImpresorasService.Worker/SpoolAcceptedWatchdogBackgroundService.cs`

Ampliar la proyección en `LoadPrinterHostsAsync` para incluir `IppSupported`, y en `ResolveIppResult` saltar directamente a `Unavailable` si la impresora tiene `IppSupported == false`:

```csharp
// En LoadPrinterHostsAsync, cambiar el tipo de retorno a Dictionary<int, (string Host, bool? IppSupported)>
private static async Task<Dictionary<int, (string Host, bool? IppSupported)>> LoadPrinterHostsAsync(
    ImpresorasDbContext db,
    List<PrintJob> candidates,
    CancellationToken ct)
{
    var printerIds = candidates
        .Where(j => j.PrinterId.HasValue)
        .Select(j => j.PrinterId!.Value)
        .Distinct().ToList();

    if (printerIds.Count == 0) return [];

    var printers = await db.Printers
        .AsNoTracking()
        .Where(p => printerIds.Contains(p.PrinterId) && p.Host != null)
        .Select(p => new { p.PrinterId, p.Host, p.IppSupported })
        .ToListAsync(ct);

    return printers
        .Where(p => !string.IsNullOrWhiteSpace(p.Host))
        .ToDictionary(p => p.PrinterId, p => (p.Host!, p.IppSupported));
}
```

```csharp
// En QueryIppForPrintersAsync, filtrar impresoras con IppSupported == false
private async Task<Dictionary<string, IppQueryResult>> QueryIppForPrintersAsync(
    Dictionary<int, (string Host, bool? IppSupported)> printerHosts,
    CancellationToken ct)
{
    if (!_options.Value.IppConfirmationEnabled || printerHosts.Count == 0) return [];

    // Solo intentar IPP en impresoras conocidas como compatibles o sin comprobar aún
    var hostsToQuery = printerHosts.Values
        .Where(p => p.IppSupported != false)
        .Select(p => p.Host)
        .Distinct().ToList();

    var tasks = hostsToQuery.Select(async host =>
        (host, result: await _ippService.QueryPrinterStateAsync(host, ct)));

    var results = await Task.WhenAll(tasks);
    return results.ToDictionary(r => r.host, r => r.result);
}
```

---

### 1.5 API — exponer el campo

**Archivo:** `src/ImpresorasService.Api/Controllers/PrintersController.cs` (o el controlador donde se listen las impresoras)

Asegurarse de que la respuesta del endpoint GET de impresoras incluya el campo `ippSupported`. Si la proyección se hace con un `select` anónimo, añadirlo:

```csharp
.Select(p => new
{
    p.PrinterId,
    p.PrinterName,
    p.SpoolQueue,
    p.Host,
    p.StoreId,
    p.IsActive,
    p.LastConnectionOk,
    p.LastConnectionTransport,
    p.IppSupported   // <-- nuevo
})
```

---

### 1.6 UI — mostrar compatibilidad en listado de impresoras

**Archivo:** `src/ImpresorasService.Web.PHP/resources/views/` (la vista de impresoras)

Añadir un badge junto al estado de conectividad de cada impresora:

```html
@if ($printer['ippSupported'] === true)
    <span class="dbx-badge dbx-badge-success" title="Soporta IPP">IPP ✓</span>
@elseif ($printer['ippSupported'] === false)
    <span class="dbx-badge dbx-badge-muted" title="No soporta IPP o sin respuesta">IPP ✗</span>
@else
    <span class="dbx-badge dbx-badge-muted" title="Compatibilidad IPP sin comprobar">IPP ?</span>
@endif
```

> **Nota:** El primer ciclo del monitor de conectividad tras el despliegue llenará el campo para todas las impresoras activas. Hasta entonces, todos los valores serán `null` (badge `?`).

---

## Bloque 2 — Sistema de alertas Telegram

### 2.1 Crear el bot en Telegram

1. Abrir Telegram y buscar el usuario **@BotFather**
2. Enviar el comando `/newbot`
3. Seguir las instrucciones: nombre del bot (ej. `ImpresorasService Alert`) y username (debe terminar en `bot`, ej. `impresorasservice_bot`)
4. BotFather devolverá un **token** con este formato: `1234567890:ABCDefGhIJKlmNoPQRsTUVwxyZ`
5. Guardar ese token — se añadirá a `appsettings.json` en la sección `Telegram:BotToken`

> **Importante:** El token es equivalente a una contraseña. No comitearlo directamente al repositorio. Usar variables de entorno en producción o un gestor de secretos.

---

### 2.2 Obtener el chat_id del destinatario

Hay dos escenarios:

**a) Chat privado con el bot (usuario individual):**
1. Buscar el bot creado en Telegram y enviarle cualquier mensaje (ej. `/start`)
2. Abrir en el navegador: `https://api.telegram.org/bot<TOKEN>/getUpdates`
3. En la respuesta JSON, buscar `result[0].message.chat.id` — ese es el `chat_id` (número positivo)

**b) Grupo o canal:**
1. Añadir el bot al grupo/canal como administrador
2. Enviar un mensaje en el grupo
3. Llamar a `getUpdates` igual que antes; el `chat_id` será un número negativo para grupos, o empezará por `-100` para supergrupos/canales

Una vez obtenido el `chat_id`, insertarlo en la BD:

```sql
INSERT INTO "printer_telegram_chat" ("chat_id", "description", "is_active", "created_at_utc")
VALUES (<CHAT_ID>, 'Grupo alertas tiendas', 1, '2026-06-17 00:00:00');
```

O bien desde la interfaz de gestión que se describe en el punto [2.9](#29-ui--gestión-de-chats-y-configuración).

---

### 2.3 Opciones de configuración (`TelegramOptions`)

**Archivo a crear:** `src/ImpresorasService.Core/Infrastructure/Options/TelegramOptions.cs`

```csharp
namespace ImpresorasService.Infrastructure.Options;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>Si false, el servicio de alertas no envía ningún mensaje aunque esté registrado.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Token del bot de Telegram obtenido de @BotFather.</summary>
    public string BotToken { get; set; } = string.Empty;
}
```

El resto de la configuración (nivel mínimo de alerta, avisar en recuperación, intervalo) se gestiona desde la BD a través de `printer_telegram_config`, no desde `appsettings.json`, para que sea editable desde la interfaz sin reiniciar el servicio.

---

### 2.4 Interfaz `ITelegramNotifier`

**Archivo a crear:** `src/ImpresorasService.Core/Application/Abstractions/ITelegramNotifier.cs`

```csharp
namespace ImpresorasService.Application.Abstractions;

public interface ITelegramNotifier
{
    /// <summary>
    /// Envía un mensaje a todos los chats activos registrados en BD.
    /// No lanza excepción si Telegram no está disponible — registra el error y continúa.
    /// </summary>
    Task SendAlertAsync(string message, CancellationToken ct);
}
```

---

### 2.5 Implementación `TelegramNotifierService`

**Archivo a crear:** `src/ImpresorasService.Core/Infrastructure/Services/TelegramNotifierService.cs`

```csharp
using System.Text;
using System.Text.Json;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Services;

internal sealed class TelegramNotifierService : ITelegramNotifier, IDisposable
{
    private readonly IOptions<TelegramOptions> _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramNotifierService> _logger;
    private readonly HttpClient _http;

    public TelegramNotifierService(
        IOptions<TelegramOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramNotifierService> logger)
    {
        _options = options;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task SendAlertAsync(string message, CancellationToken ct)
    {
        if (!_options.Value.Enabled || string.IsNullOrWhiteSpace(_options.Value.BotToken))
        {
            _logger.LogDebug("Telegram desactivado o sin token configurado. Mensaje omitido.");
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

        var chatIds = await db.TelegramChats
            .AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => c.ChatId)
            .ToListAsync(ct);

        if (chatIds.Count == 0)
        {
            _logger.LogDebug("No hay chats de Telegram activos registrados.");
            return;
        }

        var url = $"https://api.telegram.org/bot{_options.Value.BotToken}/sendMessage";

        foreach (var chatId in chatIds)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    chat_id = chatId,
                    text = message,
                    parse_mode = "HTML"
                });

                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(url, content, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning(
                        "Telegram rechazó mensaje a chat {ChatId}: {Status} — {Body}",
                        chatId, (int)response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                // No propagar: un fallo de Telegram nunca debe interrumpir el Worker.
                _logger.LogWarning(ex, "Error enviando alerta Telegram a chat {ChatId}.", chatId);
            }
        }
    }

    public void Dispose() => _http.Dispose();
}
```

---

### 2.6 Extraer `ComputeHealth` a Core

Actualmente la lógica de cálculo de salud vive en `DashboardController.cs` como un método privado estático. Hay que moverla a Core para que el Worker pueda usarla sin depender de la capa API.

**Archivo a crear:** `src/ImpresorasService.Core/Application/Services/StoreHealthEvaluator.cs`

```csharp
namespace ImpresorasService.Application.Services;

public static class StoreHealthEvaluator
{
    /// <summary>
    /// Calcula el estado de salud de una tienda con los mismos criterios que el dashboard.
    /// Devuelve ("healthy" | "warning" | "critical", motivo legible).
    /// </summary>
    public static (string Health, string Reason) Compute(
        int connectedPrinters,
        int queuedCurrent,
        int failedWithoutRetryCurrent,
        int missingHost,
        int connWarn,
        int connCrit,
        int warningQueueMin,
        int criticalQueueMin,
        int warningFailedMin,
        int criticalFailedMin,
        int missingHostMin,
        int connWarningMin,
        int connCriticalMin,
        string connCritSeverity = "critical",
        string failedCritSeverity = "critical",
        string queueCritSeverity = "critical",
        string connWarnSeverity = "warning",
        string missingHostSeverity = "warning",
        string failedWarnSeverity = "warning",
        string queueWarnSeverity = "warning")
    {
        if (connCrit > 0)
            return (ToHealth(connCritSeverity), "Impresora(s) sin conexion (conectividad)");

        if (connectedPrinters == 0 && queuedCurrent > 0)
            return ("critical", "Hay cola pero no hay impresoras activas");

        if (failedWithoutRetryCurrent >= criticalFailedMin)
            return (ToHealth(failedCritSeverity), $"Acumula {criticalFailedMin} o mas fallos sin reenviar");

        if (queuedCurrent >= criticalQueueMin)
            return (ToHealth(queueCritSeverity), $"Cola actual mayor o igual a {criticalQueueMin} trabajos");

        if (connectedPrinters == 0)
            return ("warning", "Sin impresoras activas en la tienda");

        if (connWarn > 0)
            return (ToHealth(connWarnSeverity), "Impresora(s) con fallos de conexion (conectividad)");

        if (missingHost >= missingHostMin && missingHost > 0)
            return (ToHealth(missingHostSeverity), "Impresora(s) sin host configurado");

        if (failedWithoutRetryCurrent >= warningFailedMin)
            return (ToHealth(failedWarnSeverity), "Tiene fallos recientes sin reenviar");

        if (queuedCurrent >= warningQueueMin)
            return (ToHealth(queueWarnSeverity), $"Cola actual entre {warningQueueMin} y {criticalQueueMin - 1} trabajos");

        return ("healthy", "Operacion dentro de umbrales");
    }

    private static string ToHealth(string severity)
    {
        var s = severity.Trim().ToLowerInvariant();
        return s == "critical" ? "critical" : (s == "warning" ? "warning" : "healthy");
    }
}
```

**Actualizar `DashboardController.cs`** para usar el evaluador compartido en lugar del método privado. Sustituir la llamada a `ComputeHealth(...)` por `StoreHealthEvaluator.Compute(...)` con los parámetros equivalentes, y eliminar el método privado `ComputeHealth` y `ToHealth` del controlador.

---

### 2.7 Servicio `StoreHealthAlertBackgroundService`

**Archivo a crear:** `src/ImpresorasService.Worker/StoreHealthAlertBackgroundService.cs`

```csharp
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Worker;

public sealed class StoreHealthAlertBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramNotifier _telegram;
    private readonly IOptions<TelegramOptions> _telegramOptions;
    private readonly ILogger<StoreHealthAlertBackgroundService> _logger;

    private static readonly PrintJobStatus[] QueueStatuses =
    [
        PrintJobStatus.Pending, PrintJobStatus.Routed,
        PrintJobStatus.Printing, PrintJobStatus.RetryScheduled
    ];

    private static readonly PrintJobStatus[] PrintedStatuses =
    [
        PrintJobStatus.SpoolAccepted, PrintJobStatus.PrintedConfirmed, PrintJobStatus.PrintedUnknown
    ];

    private static readonly PrintJobStatus[] FailedStatuses = [PrintJobStatus.ErrorFinal];

    public StoreHealthAlertBackgroundService(
        IServiceScopeFactory scopeFactory,
        ITelegramNotifier telegram,
        IOptions<TelegramOptions> telegramOptions,
        ILogger<StoreHealthAlertBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _telegram = telegram;
        _telegramOptions = telegramOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Espera inicial para dejar que el resto de servicios arranquen.
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

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
                _logger.LogWarning(ex, "Fallo en el servicio de alertas de tiendas.");
            }

            var intervalMinutes = await GetCheckIntervalAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, intervalMinutes)), stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

        // Cargar configuración desde BD
        var config = await db.TelegramConfigs
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, ct);

        if (config is null || !_telegramOptions.Value.Enabled)
            return;

        var minSeverity = config.MinSeverity.Trim().ToLowerInvariant();
        var notifyOnRecovery = config.NotifyOnRecovery;

        // Cargar thresholds
        var thresholdRow = await db.DashboardThresholds
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, ct);

        int warnQueue = thresholdRow?.WarningQueueMin ?? 10;
        int critQueue = thresholdRow?.CriticalQueueMin ?? 30;
        int warnFailed = thresholdRow?.WarningFailedWithoutRetryMin ?? 1;
        int critFailed = thresholdRow?.CriticalFailedWithoutRetryMin ?? 5;
        int missingHostMin = thresholdRow?.MissingHostMin ?? 1;
        int connWarnMin = thresholdRow?.ConnWarningFailuresMin ?? 2;
        int connCritMin = thresholdRow?.ConnCriticalFailuresMin ?? 3;

        // Calcular salud por tienda (misma lógica que el dashboard)
        var activeOnly = true;
        var stores = await db.Stores.AsNoTracking()
            .Where(s => s.IsActive == activeOnly)
            .Select(s => new { s.StoreId, s.Name })
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var windowStart = now.Date; // ventana: hoy

        foreach (var store in stores)
        {
            var connected = await db.Printers.AsNoTracking()
                .CountAsync(p => p.IsActive == activeOnly && p.StoreId == store.StoreId, ct);

            var connStats = await db.Printers.AsNoTracking()
                .Where(p => p.IsActive == activeOnly && p.StoreId == store.StoreId)
                .Select(p => new
                {
                    MissingHost = (p.Host == null || p.Host == "") ? 1 : 0,
                    ConnWarn = p.ConnectionFailuresStreak >= connWarnMin ? 1 : 0,
                    ConnCrit = p.ConnectionFailuresStreak >= connCritMin ? 1 : 0,
                })
                .ToListAsync(ct);

            var missingHost = connStats.Sum(x => x.MissingHost);
            var connWarn = connStats.Sum(x => x.ConnWarn);
            var connCrit = connStats.Sum(x => x.ConnCrit);

            var queued = await db.PrintJobs.AsNoTracking()
                .CountAsync(j => j.StoreId == store.StoreId && QueueStatuses.Contains(j.Status), ct);

            var failed = await db.PrintJobs.AsNoTracking()
                .CountAsync(j => j.StoreId == store.StoreId
                    && j.CreatedAtUtc >= windowStart
                    && FailedStatuses.Contains(j.Status), ct);

            var (health, reason) = StoreHealthEvaluator.Compute(
                connected, queued, failed, missingHost, connWarn, connCrit,
                warnQueue, critQueue, warnFailed, critFailed, missingHostMin, connWarnMin, connCritMin,
                thresholdRow?.ConnCriticalSeverity ?? "critical",
                thresholdRow?.FailedCriticalSeverity ?? "critical",
                thresholdRow?.QueueCriticalSeverity ?? "critical",
                thresholdRow?.ConnWarningSeverity ?? "warning",
                thresholdRow?.MissingHostSeverity ?? "warning",
                thresholdRow?.FailedWarningSeverity ?? "warning",
                thresholdRow?.QueueWarningSeverity ?? "warning");

            await ProcessStoreAlertAsync(db, store.StoreId, store.Name, health, reason,
                queued, failed, minSeverity, notifyOnRecovery, now, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ProcessStoreAlertAsync(
        ImpresorasDbContext db,
        int storeId,
        string storeName,
        string currentHealth,
        string reason,
        int queued,
        int failed,
        string minSeverity,
        bool notifyOnRecovery,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var alertState = await db.StoreAlertStates
            .SingleOrDefaultAsync(s => s.StoreId == storeId, ct);

        if (alertState is null)
        {
            alertState = new StoreAlertState { StoreId = storeId };
            await db.StoreAlertStates.AddAsync(alertState, ct);
        }

        var previousNotifiedHealth = alertState.NotifiedHealth ?? "healthy";
        var shouldNotify = false;
        string? message = null;

        bool isAlertLevel = SeverityReached(currentHealth, minSeverity);
        bool wasAlertLevel = SeverityReached(previousNotifiedHealth, minSeverity);

        if (isAlertLevel && !wasAlertLevel)
        {
            // Transición a estado de alerta
            shouldNotify = true;
            var icon = currentHealth == "critical" ? "🔴" : "🟡";
            message =
                $"{icon} <b>ALERTA {currentHealth.ToUpperInvariant()}</b> — Tienda: <b>{storeName}</b> (#{storeId})\n" +
                $"📋 {reason}\n" +
                $"📦 Cola: {queued} | ❌ Fallos: {failed}\n" +
                $"🕒 {now:yyyy-MM-dd HH:mm} UTC";
        }
        else if (!isAlertLevel && wasAlertLevel && notifyOnRecovery)
        {
            // Recuperación
            shouldNotify = true;
            message =
                $"✅ <b>RECUPERADA</b> — Tienda: <b>{storeName}</b> (#{storeId})\n" +
                $"Estado anterior: {previousNotifiedHealth} → ahora: saludable\n" +
                $"🕒 {now:yyyy-MM-dd HH:mm} UTC";
        }

        alertState.LastHealth = currentHealth;
        alertState.CheckedAtUtc = now;

        if (shouldNotify && message is not null)
        {
            await _telegram.SendAlertAsync(message, ct);
            alertState.NotifiedHealth = currentHealth;
            alertState.NotifiedAtUtc = now;

            _logger.LogInformation(
                "Alerta Telegram enviada para tienda {StoreId} ({StoreName}): {Health}.",
                storeId, storeName, currentHealth);
        }
    }

    private static bool SeverityReached(string health, string minSeverity)
    {
        return minSeverity switch
        {
            "warning" => health is "warning" or "critical",
            "critical" => health == "critical",
            _ => health == "critical"
        };
    }

    private async Task<int> GetCheckIntervalAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            var config = await db.TelegramConfigs
                .AsNoTracking()
                .Select(c => c.CheckIntervalMinutes)
                .SingleOrDefaultAsync(ct);
            return config > 0 ? config : 5;
        }
        catch
        {
            return 5;
        }
    }
}
```

---

### 2.8 Registro en DI y `Program.cs`

**Archivo:** `src/ImpresorasService.Core/Infrastructure/DependencyInjection.cs`

Añadir en `AddInfrastructure`:

```csharp
services.Configure<TelegramOptions>(configuration.GetSection(TelegramOptions.SectionName));
services.AddSingleton<ITelegramNotifier, TelegramNotifierService>();
```

**Archivo:** `src/ImpresorasService.Worker/Program.cs`

Añadir el nuevo BackgroundService:

```csharp
builder.Services.AddHostedService<StoreHealthAlertBackgroundService>();
```

---

### 2.9 UI — gestión de chats y configuración

Se necesitan dos vistas nuevas en la aplicación PHP. Ambas se comunican con la API mediante `ApiClient`.

#### Vista 1: Configuración global de alertas

**Ruta sugerida:** `/alertas/configuracion`

Permite editar la fila única de `printer_telegram_config`:
- **Nivel mínimo de alerta:** selector `warning` / `critical`
- **Avisar en recuperación:** checkbox
- **Intervalo de chequeo:** número (minutos)

#### Vista 2: Gestión de chats de Telegram

**Ruta sugerida:** `/alertas/chats`

Tabla con los chats registrados, con columnas:
- Chat ID
- Descripción
- Activo (toggle)
- Fecha de alta
- Botón de eliminar

Formulario para añadir un nuevo chat (chat_id + descripción).

#### Controlador PHP sugerido

`AlertasController.php` — ya existe en el proyecto. Ampliarlo con los métodos:
- `configuracion()` — GET: muestra formulario de config
- `guardarConfiguracion()` — POST: guarda cambios
- `chats()` — GET: lista de chats
- `agregarChat()` — POST: añade nuevo chat
- `eliminarChat()` — DELETE/POST: desactiva o borra un chat

---

### 2.10 API — endpoints de gestión Telegram

Se necesitan endpoints en la API .NET para que el frontend PHP pueda gestionar la configuración.

**Archivo a crear:** `src/ImpresorasService.Api/Controllers/TelegramController.cs`

Endpoints mínimos:

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/api/telegram/config` | Devuelve configuración actual |
| `PUT` | `/api/telegram/config` | Actualiza configuración |
| `GET` | `/api/telegram/chats` | Lista chats activos e inactivos |
| `POST` | `/api/telegram/chats` | Registra nuevo chat |
| `DELETE` | `/api/telegram/chats/{chatId}` | Elimina un chat |
| `POST` | `/api/telegram/test` | Envía mensaje de prueba a todos los chats activos |

El endpoint `POST /api/telegram/test` es especialmente útil para verificar que el bot y los chat IDs funcionan correctamente antes de depender del sistema en producción.

---

## Configuración final de `appsettings.json`

Una vez creado el bot, el archivo `appsettings.json` del Worker debe quedar así:

```json
"Telegram": {
  "Enabled": true,
  "BotToken": "1234567890:ABCDefGhIJKlmNoPQRsTUVwxyZ"
}
```

El resto de parámetros (nivel de alerta, intervalo, avisar en recuperación) se gestionan desde la BD a través de `printer_telegram_config` y no requieren reinicio del Worker al cambiarlos.

> En producción, **nunca** poner el token en el repositorio. Usar una variable de entorno:
> ```
> Telegram__BotToken=1234567890:ABCDefGhIJKlmNoPQRsTUVwxyZ
> ```
> .NET lee automáticamente variables de entorno con `__` como separador de secciones.

---

## Plan de pruebas

### Bloque 1 — IPP

| Prueba | Resultado esperado |
|---|---|
| Reiniciar el Worker y esperar un ciclo del monitor (30s) | `ipp_supported` se rellena en BD para todas las impresoras activas |
| Impresora con puerto 631 abierto | `ipp_supported = 1` |
| Impresora solo con puerto 9100 | `ipp_supported = 0` |
| Impresora sin host | `ipp_supported = null` (no cambia) |
| Job en SpoolAccepted con impresora `ipp_supported = 0` | Watchdog salta IPP y va directo a `PrintedUnknown` |
| Job en SpoolAccepted con impresora `ipp_supported = 1` | Watchdog consulta IPP y puede llegar a `PrintedConfirmed` |
| UI listado impresoras | Badge IPP visible para cada impresora |

### Bloque 2 — Telegram

| Prueba | Resultado esperado |
|---|---|
| `Telegram:Enabled = false` | No se envía ningún mensaje aunque haya alertas |
| `Telegram:Enabled = true` pero sin chats en BD | Log "No hay chats activos", sin errores |
| Token inválido | Log de warning con código de error de Telegram, el Worker no cae |
| Endpoint `POST /api/telegram/test` | Mensaje "Prueba de conexión" llega al chat |
| Tienda pasa a critical (simular subiendo la cola) | Mensaje de alerta llega a Telegram |
| Tienda se recupera | Mensaje de recuperación llega (si `notify_on_recovery = true`) |
| Tienda ya estaba en critical → sigue en critical | **No** se envía segundo mensaje (sin spam) |
| Cambiar `min_severity` de `critical` a `warning` en BD | En el siguiente ciclo, las tiendas en warning también generan alerta |

---

## Despliegue

1. Solicitar al DBA que ejecute el SQL del correo enviado
2. Verificar en HANA Studio que las 4 tablas/columnas existen correctamente
3. Implementar los cambios de código descritos en este documento
4. Compilar y ejecutar las pruebas de integración
5. Parar el Worker en producción
6. Desplegar el nuevo binario del Worker
7. Arrancar el Worker y revisar logs los primeros 2-3 minutos:
   - El monitor de conectividad debe rellenar `ipp_supported` en el primer ciclo (~30s)
   - El servicio de alertas debe arrancar y hacer el primer chequeo (~15s de retraso inicial)
8. Crear el bot con @BotFather y obtener el token
9. Obtener el `chat_id` del grupo/usuario destinatario
10. Insertar el `chat_id` en BD (UI o SQL directo)
11. Actualizar `appsettings.json` con el token y poner `Enabled: true`
12. Reiniciar el Worker
13. Ejecutar `POST /api/telegram/test` para verificar la conectividad end-to-end
