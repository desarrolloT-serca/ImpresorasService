using System.Text.Json;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain.Entities;

namespace ImpresorasService.Application.Abstractions;

public enum IppOutcome
{
    PrinterIdle,       // estado 3: impresora libre → trabajo confirmado
    PrinterProcessing, // estado 4: imprimiendo → esperar a que termine
    PrinterStopped,    // estado 5: detenida (sin papel, atasco…) → esperar recuperación
    Unavailable,       // sin respuesta IPP o no soportado
    JobAborted,        // el trabajo enviado por IP acabó cancelado o abortado en la impresora
    CompletedWithErrors // la impresora lo dio por terminado, pero con errores
}

public record IppQueryResult(IppOutcome Outcome, string? ErrorCode = null, string? ErrorMessage = null)
{
    /// <summary>Lo que dijo la impresora, si respondió por IPP (solo <see cref="IIppConfirmationService.QueryPrinterStateAsync"/>).</summary>
    public IppEstadoImpresora? Estado { get; init; }
}

/// <summary>
/// Estado de la impresora por Get-Printer-Attributes (RFC 8011 y PWG 5100.13). Cada dato es null si la
/// impresora no lo da (ERP_SERCA docs/72, R0: la Canon no da mensaje; las Brother, el aviso del panel).
/// </summary>
public sealed record IppEstadoImpresora(
    int? State,
    string? Reasons,
    string? Message,
    int? QueuedJobs,
    IReadOnlyList<IppConsumible> Consumibles,
    string? MakeAndModel,
    string? MoreInfoUrl)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Para la ficha (docs/72, mejora 6): con init para no tocar a quien ya construye el estado.
    public string? Firmware { get; init; }
    /// <summary>printer-up-time: segundos desde que se encendió.</summary>
    public int? EncendidaSegundos { get; init; }
    public IReadOnlyList<IppBandeja> Bandejas { get; init; } = [];

    /// <summary>Las propiedades de <see cref="Printer"/> que escribe <see cref="AplicarA"/> (para marcarlas modificadas).</summary>
    public static readonly string[] Propiedades =
    [
        nameof(Printer.IppState), nameof(Printer.IppStateReasons), nameof(Printer.IppStateMessage),
        nameof(Printer.IppQueuedJobs), nameof(Printer.IppMarkersJson), nameof(Printer.IppMakeAndModel),
        nameof(Printer.IppMoreInfoUrl), nameof(Printer.IppCheckedAtUtc), nameof(Printer.IppStateSinceUtc),
        nameof(Printer.IppFirmware), nameof(Printer.IppUpSinceUtc), nameof(Printer.IppTraysJson)
    ];

    /// <summary>
    /// Guarda esta lectura en la impresora; <c>null</c> la borra (host nuevo: es otra impresora).
    /// <paramref name="desdeUtc"/>: desde cuándo dura este estado (<see cref="Desde"/>); sin él, desde ahora.
    /// </summary>
    public static void AplicarA(Printer printer, IppEstadoImpresora? estado, DateTimeOffset? leidoUtc,
        DateTimeOffset? desdeUtc = null)
    {
        printer.IppState = estado?.State;
        printer.IppStateReasons = estado?.Reasons;
        printer.IppStateMessage = estado?.Message;
        printer.IppQueuedJobs = estado?.QueuedJobs;
        printer.IppMarkersJson = estado?.Consumibles is { Count: > 0 } c ? JsonSerializer.Serialize(c, Json) : null;
        printer.IppMakeAndModel = estado?.MakeAndModel;
        printer.IppMoreInfoUrl = estado?.MoreInfoUrl;
        printer.IppCheckedAtUtc = estado is null ? null : leidoUtc;
        printer.IppStateSinceUtc = estado is null ? null : desdeUtc ?? leidoUtc;
        printer.IppFirmware = estado?.Firmware;
        // Al segundo: printer-up-time va en segundos y así no cambia en cada lectura.
        printer.IppUpSinceUtc = estado?.EncendidaSegundos is { } s && leidoUtc is { } l
            ? DateTimeOffset.FromUnixTimeSeconds(l.ToUnixTimeSeconds() - s)
            : null;
        printer.IppTraysJson = estado?.Bandejas is { Count: > 0 } b ? JsonSerializer.Serialize(b, Json) : null;
    }

    /// <summary>
    /// Desde cuándo dura el estado (estado y motivos) que trae esta lectura: el de antes si no ha cambiado, si
    /// no, ahora. Es el «Desde» de las incidencias de la impresora (docs/72, mejora 3).
    /// </summary>
    public DateTimeOffset Desde(int? estadoAnterior, string? motivosAnteriores, DateTimeOffset? desdeAnterior,
        DateTimeOffset ahora) =>
        desdeAnterior is { } d && estadoAnterior == State && motivosAnteriores == Reasons ? d : ahora;

    // ponytail: umbral fijo; el monitor lee cada 30 s (PrinterConnectivity:IntervalSeconds). Si alguien
    // sube ese intervalo por encima de unos minutos, este valor tiene que salir de la misma opción.
    public static readonly TimeSpan LecturaVieja = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gravedad (las de <see cref="Domain.Connectivity.PrinterConnectivityState"/>) y texto en castellano de la
    /// última lectura (ERP_SERCA docs/72, R2). Por el sufijo de cada motivo (RFC 8011 §5.4.12): -error
    /// o sin sufijo, crítica; -warning, aviso; -report, nada. Tóner por los umbrales de la pantalla
    /// Umbrales (<see cref="UmbralToner"/>, 10/11/2026): aviso y crítico. Sin lectura o con más de
    /// <see cref="LecturaVieja"/>, neutral: no se sabe.
    /// </summary>
    public static (string Gravedad, string? Texto) Describir(Printer p, DateTimeOffset ahora, UmbralToner toner)
    {
        const string critica = Domain.Connectivity.PrinterConnectivityState.SeverityCritical;
        const string aviso = Domain.Connectivity.PrinterConnectivityState.SeverityWarning;
        if (p.IppCheckedAtUtc is not { } leido)
            return (Domain.Connectivity.PrinterConnectivityState.SeverityNeutral, null);
        if (ahora - leido > LecturaVieja)
            return (Domain.Connectivity.PrinterConnectivityState.SeverityNeutral, "Sin lectura reciente");

        var gravedad = Domain.Connectivity.PrinterConnectivityState.SeverityHealthy;
        void Sube(string g) { if (g == critica || gravedad != critica) gravedad = g; }
        var textos = new List<string>();
        foreach (var motivo in (p.IppStateReasons ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (motivo.EndsWith("-report", StringComparison.OrdinalIgnoreCase)) continue;
            Sube(motivo.EndsWith("-warning", StringComparison.OrdinalIgnoreCase) ? aviso : critica);
            textos.Add(Domain.ErroresTrabajo.MotivoIpp(motivo) ?? motivo);
        }
        var consumibles = p.IppMarkersJson is null
            ? []
            : JsonSerializer.Deserialize<List<IppConsumible>>(p.IppMarkersJson, Json) ?? [];
        foreach (var c in consumibles)
            if (toner.Gravedad(c.Nivel) is ("warning" or "critical") and var g)
            {
                Sube(g);
                textos.Add($"{c.Nombre ?? "Consumible"} al {c.Nivel} %");
            }
        if (p.IppState == 5)
        {
            Sube(critica);
            if (textos.Count == 0) textos.Add("La impresora está detenida");
        }

        var texto = textos.Count > 0 ? string.Join(". ", textos.Distinct())
            : p.IppState == 4 ? "Imprimiendo"
            : "Lista";
        return (gravedad, texto);
    }
}

/// <summary>Un consumible (marker-*): nivel y umbral bajo en %, o -1/-2/-3 (sin dato, desconocido, queda algo).</summary>
public sealed record IppConsumible(string? Nombre, string? Color, string? Tipo, int? Nivel, int? Bajo);

/// <summary>
/// Una bandeja de entrada (printer-input-tray, PWG 5100.13): hojas que caben y que hay, o -2/-3 (desconocido,
/// «hay algo»). La Canon da más hojas que capacidad en el cajón (1100 de 550): se enseña tal cual.
/// </summary>
public sealed record IppBandeja(string? Nombre, int? Capacidad, int? Nivel);

public interface IIppConfirmationService
{
    /// <summary>
    /// Consulta el estado IPP del printer en <paramref name="printerHost"/>.
    /// Devuelve <see cref="IppOutcome.PrinterIdle"/> si el printer está disponible y sin error,
    /// <see cref="IppOutcome.PrinterStopped"/> si el printer reporta un estado detenido/error,
    /// o <see cref="IppOutcome.Unavailable"/> si IPP no responde o no está disponible.
    /// </summary>
    Task<IppQueryResult> QueryPrinterStateAsync(string printerHost, CancellationToken ct);

    /// <summary>
    /// Imprime el PDF directamente en la impresora por IPP (Print-Job), sin cola de Windows.
    /// Si no ha salido nada (no responde, no acepta el formato, rechaza el trabajo) devuelve el
    /// motivo con <c>SinEnviar</c>, y se puede probar por la cola; <c>null</c> si el host no vale.
    /// Si el envío se corta a medias devuelve <c>NET_TIMEOUT</c>, y si la impresora falla con el
    /// trabajo a medias, <c>IPP_ERROR_WHILE_PRINTING</c>: puede haberse impreso y no se reintenta.
    /// <paramref name="comoImagen"/>: la impresora ya abortó este documento en PDF; se manda como imagen.
    /// </summary>
    Task<PrintSpoolResult?> PrintPdfAsync(string printerHost, byte[] pdf, string jobName, bool comoImagen, CancellationToken ct);

    /// <summary>
    /// Estado de un trabajo enviado por IP (Get-Job-Attributes): terminado = <see cref="IppOutcome.PrinterIdle"/>,
    /// en cola o imprimiendo = <see cref="IppOutcome.PrinterProcessing"/>, detenido =
    /// <see cref="IppOutcome.PrinterStopped"/>, cancelado o abortado = <see cref="IppOutcome.JobAborted"/>.
    /// <c>null</c> si la impresora no responde o ya no lo recuerda: entonces vale el estado de la impresora.
    /// </summary>
    Task<IppQueryResult?> QueryJobStateAsync(string printerHost, int jobId, CancellationToken ct);

    /// <summary>
    /// Cancela en la impresora un trabajo enviado por IP, al cancelarlo una persona en la aplicación
    /// (decisión del usuario, 27/10/2026: si no, sale al recuperarse la impresora y duplica en silencio).
    /// Antes comprueba por su nombre que ese número sigue siendo el nuestro: una impresora que se
    /// reinicia puede reutilizar números y se cancelaría otro documento (3b.8).
    /// </summary>
    Task<CancelacionEnImpresora> CancelJobAsync(string printerHost, int jobId, string jobName, CancellationToken ct);

    /// <summary>
    /// Identify-Printer (PWG 5100.13): la impresora pita o parpadea para encontrarla (docs/72, mejora 4).
    /// <c>true</c> si lo acepta, <c>false</c> si no lo admite, <c>null</c> si no responde. Por defecto,
    /// <c>null</c>: así los dobles de prueba que no lo necesitan no tienen que implementarlo.
    /// </summary>
    Task<bool?> IdentifyAsync(string printerHost, CancellationToken ct) => Task.FromResult<bool?>(null);

    /// <summary>
    /// El job-name con que se manda un trabajo a la impresora (como mucho 100 caracteres). El mismo al
    /// mandarlo y al comprobar que un número de trabajo sigue siendo el nuestro antes de cancelarlo.
    /// </summary>
    static string JobNameFor(string documentType, string externalJobId)
    {
        var nombre = $"{documentType} {externalJobId}";
        return nombre.Length > 100 ? nombre[..100] : nombre;
    }
}

/// <summary>Qué pasó al cancelar en la impresora un trabajo que se cancela en la aplicación.</summary>
public enum CancelacionEnImpresora
{
    /// <summary>Estaba en su cola y lo ha cancelado: no saldrá.</summary>
    Cancelado,
    /// <summary>Ya lo había impreso (job-state 9).</summary>
    YaSalio,
    /// <summary>Ya estaba cancelado o abortado (job-state 7-8): no saldrá.</summary>
    YaNoSaldra,
    /// <summary>No lo tiene en su cola (lo olvidó o se reinició): no saldrá; si salió, fue antes.</summary>
    NoLoTiene,
    /// <summary>Ese número es ahora otro documento: no se ha tocado.</summary>
    NoEsNuestro,
    /// <summary>No devuelve el nombre del trabajo: no se puede comprobar que sea el nuestro, no se toca.</summary>
    SinNombre,
    /// <summary>No responde.</summary>
    SinRespuesta,
    /// <summary>Lo tiene pero no deja cancelarlo por la red.</summary>
    NoDeja
}
