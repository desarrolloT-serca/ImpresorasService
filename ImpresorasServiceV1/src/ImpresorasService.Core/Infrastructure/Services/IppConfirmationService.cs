using System.Net.Http.Headers;
using System.Text;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Services;

internal sealed class IppConfirmationService : IIppConfirmationService, IDisposable
{
    // printer-state values per RFC 8011 §5.4.11
    private const int StateIdle = 3;
    private const int StateProcessing = 4;
    private const int StateStopped = 5;

    private readonly HttpClient _http;
    private readonly IOptions<PrintExecutionOptions> _options;
    private readonly ILogger<IppConfirmationService> _logger;

    public IppConfirmationService(
        IOptions<PrintExecutionOptions> options,
        ILogger<IppConfirmationService> logger)
    {
        _options = options;
        _logger = logger;
        // Singleton HttpClient con PooledConnectionLifetime para refrescar DNS/conexiones TCP.
        // Timeout.InfiniteTimeSpan: el CancellationToken controla el timeout por llamada.
        _http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
            { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<IppQueryResult> QueryPrinterStateAsync(string printerHost, CancellationToken ct)
    {
        var ip = ParseHost(printerHost);
        if (ip is null)
            return new IppQueryResult(IppOutcome.Unavailable, null, "Host de impresora no válido.");

        var timeoutMs = _options.Value.IppTimeoutMs;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeoutMs);

        try
        {
            var uri = new Uri($"http://{ip}:631/ipp/printer");
            var payload = BuildGetPrinterAttributesRequest(ip);

            var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/ipp");

            var response = await _http.PostAsync(uri, content, linked.Token);
            var bytes = await response.Content.ReadAsByteArrayAsync(linked.Token);

            var (state, reasons) = ParseResponse(bytes);

            if (state == StateStopped)
            {
                var errorCode = reasons?.Split(',').FirstOrDefault()?.Trim() ?? "IPP_PRINTER_STOPPED";
                var msg = TranslateReasons(reasons);
                _logger.LogWarning("IPP: {Host} detenida — {Reasons}.", ip, reasons);
                return new IppQueryResult(IppOutcome.PrinterStopped, errorCode, msg);
            }

            if (state == StateIdle)
            {
                _logger.LogInformation("IPP: {Host} idle → trabajo confirmado.", ip);
                return new IppQueryResult(IppOutcome.PrinterIdle);
            }

            if (state == StateProcessing)
            {
                _logger.LogInformation("IPP: {Host} processing → esperando que termine.", ip);
                return new IppQueryResult(IppOutcome.PrinterProcessing);
            }

            _logger.LogDebug("IPP: {Host} estado desconocido ({State}).", ip, state?.ToString() ?? "null");
            return new IppQueryResult(IppOutcome.Unavailable, null, $"IPP: estado inesperado ({state}).");
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("IPP timeout ({TimeoutMs}ms) para {Host}.", timeoutMs, ip);
            return new IppQueryResult(IppOutcome.Unavailable, null, $"IPP timeout ({timeoutMs}ms).");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IPP no disponible para {Host}.", ip);
            return new IppQueryResult(IppOutcome.Unavailable, null, ex.Message[..Math.Min(ex.Message.Length, 200)]);
        }
    }

    private static string? ParseHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        host = host.Trim();
        return Uri.TryCreate(host, UriKind.Absolute, out var uri) ? uri.Host : host;
    }

    // --- IPP binary encoding (RFC 8010) ---

    private static byte[] BuildGetPrinterAttributesRequest(string ip)
    {
        var ms = new MemoryStream();

        ms.WriteByte(0x01); ms.WriteByte(0x01);   // version 1.1
        ms.WriteByte(0x00); ms.WriteByte(0x0B);   // operation: Get-Printer-Attributes
        ms.Write([0x00, 0x00, 0x00, 0x01]);        // request-id = 1
        ms.WriteByte(0x01);                        // begin-attribute-group: operation-attributes

        WriteAttr(ms, 0x47, "attributes-charset", "utf-8");
        WriteAttr(ms, 0x48, "attributes-natural-language", "en-us");
        WriteAttr(ms, 0x45, "printer-uri", $"ipp://{ip}:631/ipp/printer");
        WriteAttr(ms, 0x44, "requested-attributes", "printer-state");
        WriteAttr(ms, 0x44, "", "printer-state-reasons"); // additional value (empty name = same attr)
        WriteAttr(ms, 0x44, "", "printer-state-message");

        ms.WriteByte(0x03); // end-of-attributes
        return ms.ToArray();
    }

    private static void WriteAttr(MemoryStream ms, byte tag, string name, string value)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        ms.WriteByte(tag);
        ms.WriteByte((byte)(nameBytes.Length >> 8)); ms.WriteByte((byte)(nameBytes.Length & 0xFF));
        ms.Write(nameBytes);
        ms.WriteByte((byte)(valueBytes.Length >> 8)); ms.WriteByte((byte)(valueBytes.Length & 0xFF));
        ms.Write(valueBytes);
    }

    // --- IPP binary parsing (RFC 8010) ---

    private static (int? state, string? reasons) ParseResponse(byte[] data)
    {
        if (data.Length < 9) return (null, null);

        var i = 8; // skip 8-byte header (version 2 + operation-id 2 + request-id 4)
        int? printerState = null;
        var reasonsList = new List<string>();
        var lastAttrName = "";

        while (i < data.Length)
        {
            var b = data[i];

            // Delimiter tags: 0x00–0x0F
            if (b <= 0x0F)
            {
                if (b == 0x03) break; // end-of-attributes
                i++;
                lastAttrName = "";
                continue;
            }

            var tag = b;
            i++;

            if (i + 2 > data.Length) break;
            int nameLen = (data[i] << 8) | data[i + 1];
            i += 2;
            if (i + nameLen > data.Length) break;
            var name = nameLen > 0 ? Encoding.UTF8.GetString(data, i, nameLen) : lastAttrName;
            if (nameLen > 0) lastAttrName = name;
            i += nameLen;

            if (i + 2 > data.Length) break;
            int valueLen = (data[i] << 8) | data[i + 1];
            i += 2;
            if (i + valueLen > data.Length) break;

            // 0x21 = integer, 0x23 = enum — printer-state es type2 enum (RFC 8011 §5.4.11)
            if (tag is 0x21 or 0x23 && name == "printer-state" && valueLen == 4)
                printerState = (data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3];
            // 0x44 = keyword → printer-state-reasons
            else if (tag == 0x44 && name == "printer-state-reasons" && valueLen > 0)
            {
                var reason = Encoding.UTF8.GetString(data, i, valueLen);
                if (reason != "none") reasonsList.Add(reason);
            }

            i += valueLen;
        }

        return (printerState, reasonsList.Count > 0 ? string.Join(", ", reasonsList) : null);
    }

    private static string TranslateReasons(string? reasons)
    {
        if (string.IsNullOrWhiteSpace(reasons))
            return "La impresora ha reportado un error desconocido.";

        var parts = reasons.Split(',')
            .Select(r => TranslateReason(StripReasonSuffix(r.Trim().ToLowerInvariant())))
            .Where(r => r != "none")
            .ToList();

        return parts.Count > 0
            ? string.Join(". ", parts) + "."
            : "La impresora ha reportado un error.";
    }

    private static string StripReasonSuffix(string reason)
    {
        foreach (var suffix in new[] { "-error", "-warning", "-report" })
            if (reason.EndsWith(suffix, StringComparison.Ordinal))
                return reason[..^suffix.Length];
        return reason;
    }

    private static string TranslateReason(string reason) => reason switch
    {
        "none"                          => "none",
        "media-empty"                   => "Sin papel en la bandeja",
        "media-jam"                     => "Atasco de papel",
        "media-low"                     => "Papel casi agotado",
        "media-needed"                  => "Se necesita cargar papel",
        "media-path-cannot-duplex"      => "No se puede imprimir a doble cara",
        "cover-open"                    => "La tapa de la impresora está abierta",
        "door-open"                     => "La puerta de la impresora está abierta",
        "interlock-open"                => "La cubierta de seguridad está abierta",
        "toner-empty"                   => "El tóner está agotado",
        "toner-low"                     => "El tóner está bajo",
        "marker-supply-empty"           => "El cartucho de tinta o tóner está vacío",
        "marker-supply-low"             => "El cartucho de tinta o tóner está bajo",
        "marker-waste-full"             => "El depósito de residuos de tóner está lleno",
        "marker-waste-almost-full"      => "El depósito de residuos de tóner está casi lleno",
        "output-area-full"              => "La bandeja de salida está llena",
        "output-area-almost-full"       => "La bandeja de salida está casi llena",
        "input-tray-missing"            => "La bandeja de papel no está colocada",
        "output-tray-missing"           => "La bandeja de salida no está colocada",
        "offline"                       => "La impresora está sin conexión (offline)",
        "paused"                        => "La impresora está en pausa",
        "moving-to-paused"              => "La impresora se está pausando",
        "shutdown"                      => "La impresora está apagada",
        "connecting-to-device"          => "No se puede establecer conexión con la impresora",
        "timed-out"                     => "Tiempo de espera agotado al conectar con la impresora",
        "spool-area-full"               => "El área de spool está llena, no hay espacio para nuevos trabajos",
        "fuser-over-temp"               => "El fusor está a temperatura excesiva",
        "fuser-under-temp"              => "El fusor no alcanza la temperatura de trabajo",
        "opc-life-over"                 => "El cartucho fotoconductor ha llegado al final de su vida útil",
        "developer-empty"               => "El revelador del cartucho está vacío",
        "developer-low"                 => "El revelador del cartucho está bajo",
        "other"                         => "La impresora ha reportado un error genérico",
        _                               => $"Error de impresora: {reason}"
    };

    public void Dispose() => _http.Dispose();
}
