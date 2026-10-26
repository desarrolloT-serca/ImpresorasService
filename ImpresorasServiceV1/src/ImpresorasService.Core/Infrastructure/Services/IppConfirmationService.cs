using System.Net.Http.Headers;
using System.Text;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// Cliente IPP (RFC 8010/8011) de las impresoras: su estado, para confirmar trabajos, y la
/// impresión directa por IP, sin cola de Windows.
/// </summary>
internal sealed class IppConfirmationService : IIppConfirmationService, IDisposable
{
    // printer-state values per RFC 8011 §5.4.11
    private const int StateIdle = 3;
    private const int StateProcessing = 4;
    private const int StateStopped = 5;

    // /ipp/print es la ruta de IPP Everywhere (Brother, Canon y HP recientes); /ipp/printer, la de
    // algunos HP antiguos. Comprobado en impresoras reales el 26/10/2026.
    private static readonly string[] Rutas = ["/ipp/print", "/ipp/printer"];

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
        try
        {
            var atributos = await GetPrinterAttributesAsync(
                ip, ["printer-state", "printer-state-reasons", "printer-state-message"], ct);
            if (atributos is null)
                return new IppQueryResult(IppOutcome.Unavailable, null, "IPP no responde.");

            var (_, respuesta) = atributos.Value;
            var state = respuesta.Entero("printer-state");
            var reasonsList = respuesta.Textos("printer-state-reasons").Where(r => r != "none").ToList();
            var reasons = reasonsList.Count > 0 ? string.Join(", ", reasonsList) : null;

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
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("IPP timeout ({TimeoutMs}ms) para {Host}.", timeoutMs, ip);
            return new IppQueryResult(IppOutcome.Unavailable, null, $"IPP timeout ({timeoutMs}ms).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "IPP no disponible para {Host}.", ip);
            return new IppQueryResult(IppOutcome.Unavailable, null, ex.Message[..Math.Min(ex.Message.Length, 200)]);
        }
    }

    public async Task<PrintSpoolResult?> PrintPdfAsync(string printerHost, byte[] pdf, string jobName, CancellationToken ct)
    {
        var ip = ParseHost(printerHost);
        if (ip is null)
            return null;

        // 1. ¿Acepta PDF y trabajos? Hasta aquí no se ha enviado nada: cualquier fallo, por la cola.
        (string Ruta, IppRespuesta Respuesta)? atributos;
        try
        {
            atributos = await GetPrinterAttributesAsync(
                ip, ["document-format-supported", "printer-is-accepting-jobs",
                    "pwg-raster-document-type-supported", "pwg-raster-document-resolution-supported"], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogInformation("IPP: {Host} no responde ({Error}); se usa la cola de Windows.", ip, ex.Message);
            return null;
        }
        if (atributos is null)
            return null;
        var (ruta, impresora) = atributos.Value;
        // PDF si lo acepta; si no, imagen PWG (IPP Everywhere), que dibuja el servicio.
        var formatos = impresora.Textos("document-format-supported").ToHashSet();
        var tipos = impresora.Textos("pwg-raster-document-type-supported").ToHashSet();
        string formato;
        byte[] documento;
        if (formatos.Contains("application/pdf"))
        {
            formato = "application/pdf";
            documento = pdf;
        }
        else if (formatos.Contains("image/pwg-raster") && (tipos.Contains("sgray_8") || tipos.Contains("srgb_8")))
        {
            var resoluciones = impresora.Enteros("pwg-raster-document-resolution-supported").ToList();
            var dpi = resoluciones.Contains(300) || resoluciones.Count == 0 ? 300 : resoluciones.Min();
            try
            {
                documento = PwgRaster.DesdePdf(pdf, dpi, color: !tipos.Contains("sgray_8"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IPP: no se pudo convertir el PDF a imagen para {Host}; se usa la cola de Windows.", ip);
                return null;
            }
            formato = "image/pwg-raster";
        }
        else
        {
            _logger.LogInformation("IPP: {Host} no acepta PDF ni imagen PWG; se usa la cola de Windows.", ip);
            return null;
        }
        if (impresora.Booleano("printer-is-accepting-jobs") == false)
        {
            _logger.LogInformation("IPP: {Host} no acepta trabajos ahora; se usa la cola de Windows.", ip);
            return null;
        }

        // 2. Print-Job con el PDF detrás de los atributos.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(_options.Value.TimeoutSeconds));
        IppRespuesta respuesta;
        try
        {
            respuesta = await PostAsync(ip, ruta, BuildPrintJobRequest(ip, ruta, jobName, documento, formato), linked.Token);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.ConnectionError)
        {
            // No llegó a conectar: el documento no salió del servidor.
            _logger.LogInformation("IPP: {Host} rechaza la conexión al imprimir; se usa la cola de Windows.", ip);
            return null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Cortado a medias: la impresora puede haberlo recibido. Parar antes que duplicar.
            _logger.LogWarning(ex, "IPP: envío a {Host} cortado sin conocerse el resultado.", ip);
            return new PrintSpoolResult(false, "NET_TIMEOUT",
                "El envío por IP se cortó sin conocerse el resultado.", false);
        }

        if (respuesta.Http != 200 || respuesta.Estado is null or >= 0x0100)
        {
            // La impresora no aceptó el trabajo: no hay nada que pueda salir duplicado.
            _logger.LogInformation("IPP: {Host} rechaza el trabajo (HTTP {Http}, IPP 0x{Estado:X4}); se usa la cola de Windows.",
                ip, respuesta.Http, respuesta.Estado ?? 0);
            return null;
        }

        var jobId = respuesta.Entero("job-id");
        _logger.LogInformation("IPP: {Host} aceptó el trabajo {JobId}.", ip, jobId);
        return new PrintSpoolResult(true, null, null, false,
            Detalle: $"La impresora aceptó el trabajo por IP ({ip}, trabajo {jobId?.ToString() ?? "sin número"}"
                + (formato == "application/pdf" ? ")" : ", como imagen)"),
            IppJobId: jobId);
    }

    // job-state (RFC 8011 §5.3.7): 3 pendiente, 4 retenido, 5 imprimiendo, 6 detenido, 7 cancelado,
    // 8 abortado, 9 terminado.
    public async Task<IppQueryResult?> QueryJobStateAsync(string printerHost, int jobId, CancellationToken ct)
    {
        var ip = ParseHost(printerHost);
        if (ip is null)
            return null;

        try
        {
            foreach (var ruta in Rutas)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(_options.Value.IppTimeoutMs);
                var r = await PostAsync(ip, ruta, BuildGetJobAttributesRequest(ip, ruta, jobId), linked.Token);
                if (r.Http != 200 || r.Estado is null or >= 0x0100)
                    continue; // ruta equivocada o trabajo olvidado (client-error-not-found)

                return EstadoDelTrabajo(r);
            }
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "IPP: sin estado del trabajo {JobId} en {Host}.", jobId, ip);
            return null;
        }
    }

    internal static IppQueryResult? EstadoDelTrabajo(IppRespuesta r)
    {
        var razones = r.Textos("job-state-reasons").Where(x => x is not "none" and not "job-completed-successfully").ToList();
        var detalle = razones.Count > 0 ? string.Join(", ", razones) : null;
        return r.Entero("job-state") switch
        {
            9 => new IppQueryResult(IppOutcome.PrinterIdle),
            3 or 4 or 5 => new IppQueryResult(IppOutcome.PrinterProcessing),
            6 => new IppQueryResult(IppOutcome.PrinterStopped, "IPP_JOB_STOPPED", TranslateReasons(detalle)),
            7 => new IppQueryResult(IppOutcome.JobAborted, "IPP_JOB_CANCELED",
                "El trabajo se canceló en la impresora" + (detalle is null ? "." : $": {detalle}.")),
            8 => new IppQueryResult(IppOutcome.JobAborted, "IPP_JOB_ABORTED",
                "La impresora abortó el trabajo" + (detalle is null ? "." : $": {detalle}.")),
            _ => null
        };
    }

    private static string? ParseHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        host = host.Trim();
        return Uri.TryCreate(host, UriKind.Absolute, out var uri) ? uri.Host : host;
    }

    // La primera ruta que responde con éxito; null si ninguna.
    private async Task<(string Ruta, IppRespuesta Respuesta)?> GetPrinterAttributesAsync(
        string ip, string[] atributos, CancellationToken ct)
    {
        foreach (var ruta in Rutas)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(_options.Value.IppTimeoutMs);
            var respuesta = await PostAsync(ip, ruta, BuildGetPrinterAttributesRequest(ip, ruta, atributos), linked.Token);
            if (respuesta.Http == 200 && respuesta.Estado is < 0x0100)
                return (ruta, respuesta);
        }
        return null;
    }

    private async Task<IppRespuesta> PostAsync(string ip, string ruta, byte[] payload, CancellationToken ct)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/ipp");
        using var response = await _http.PostAsync(new Uri($"http://{ip}:631{ruta}"), content, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return ParseResponse((int)response.StatusCode, bytes);
    }

    // --- IPP binary encoding (RFC 8010) ---

    internal static byte[] BuildGetPrinterAttributesRequest(string ip, string ruta, string[] atributos)
    {
        var ms = new MemoryStream();

        ms.WriteByte(0x01); ms.WriteByte(0x01);   // version 1.1
        ms.WriteByte(0x00); ms.WriteByte(0x0B);   // operation: Get-Printer-Attributes
        ms.Write([0x00, 0x00, 0x00, 0x01]);        // request-id = 1
        ms.WriteByte(0x01);                        // begin-attribute-group: operation-attributes

        WriteAttr(ms, 0x47, "attributes-charset", "utf-8");
        WriteAttr(ms, 0x48, "attributes-natural-language", "en-us");
        WriteAttr(ms, 0x45, "printer-uri", $"ipp://{ip}:631{ruta}");
        for (var i = 0; i < atributos.Length; i++)
            WriteAttr(ms, 0x44, i == 0 ? "requested-attributes" : "", atributos[i]); // "" = otro valor del mismo

        ms.WriteByte(0x03); // end-of-attributes
        return ms.ToArray();
    }

    internal static byte[] BuildGetJobAttributesRequest(string ip, string ruta, int jobId)
    {
        var ms = new MemoryStream();

        ms.WriteByte(0x01); ms.WriteByte(0x01);   // version 1.1
        ms.WriteByte(0x00); ms.WriteByte(0x09);   // operation: Get-Job-Attributes
        ms.Write([0x00, 0x00, 0x00, 0x01]);        // request-id = 1
        ms.WriteByte(0x01);                        // begin-attribute-group: operation-attributes

        WriteAttr(ms, 0x47, "attributes-charset", "utf-8");
        WriteAttr(ms, 0x48, "attributes-natural-language", "en-us");
        WriteAttr(ms, 0x45, "printer-uri", $"ipp://{ip}:631{ruta}");
        ms.WriteByte(0x21);                        // integer
        var nombre = Encoding.UTF8.GetBytes("job-id");
        ms.WriteByte(0x00); ms.WriteByte((byte)nombre.Length); ms.Write(nombre);
        ms.WriteByte(0x00); ms.WriteByte(0x04);
        ms.Write([(byte)(jobId >> 24), (byte)(jobId >> 16), (byte)(jobId >> 8), (byte)jobId]);
        WriteAttr(ms, 0x44, "requested-attributes", "job-state");
        WriteAttr(ms, 0x44, "", "job-state-reasons");

        ms.WriteByte(0x03); // end-of-attributes
        return ms.ToArray();
    }

    internal static byte[] BuildPrintJobRequest(string ip, string ruta, string jobName, byte[] documento,
        string formato = "application/pdf")
    {
        var ms = new MemoryStream();

        ms.WriteByte(0x01); ms.WriteByte(0x01);   // version 1.1
        ms.WriteByte(0x00); ms.WriteByte(0x02);   // operation: Print-Job
        ms.Write([0x00, 0x00, 0x00, 0x01]);        // request-id = 1
        ms.WriteByte(0x01);                        // begin-attribute-group: operation-attributes

        WriteAttr(ms, 0x47, "attributes-charset", "utf-8");
        WriteAttr(ms, 0x48, "attributes-natural-language", "en-us");
        WriteAttr(ms, 0x45, "printer-uri", $"ipp://{ip}:631{ruta}");
        WriteAttr(ms, 0x42, "requesting-user-name", "ImpresorasService");
        WriteAttr(ms, 0x42, "job-name", jobName.Length > 100 ? jobName[..100] : jobName);
        WriteAttr(ms, 0x49, "document-format", formato);

        ms.WriteByte(0x03); // end-of-attributes; detrás va el documento
        ms.Write(documento);
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

    internal sealed record IppRespuesta(int Http, int? Estado, Dictionary<string, List<object>> Valores)
    {
        public IEnumerable<string> Textos(string nombre) =>
            Valores.TryGetValue(nombre, out var v) ? v.OfType<string>() : [];
        public IEnumerable<int> Enteros(string nombre) =>
            Valores.TryGetValue(nombre, out var v) ? v.OfType<int>() : [];
        public int? Entero(string nombre) =>
            Valores.TryGetValue(nombre, out var v) && v.OfType<int>().Any() ? v.OfType<int>().First() : null;
        public bool? Booleano(string nombre) =>
            Valores.TryGetValue(nombre, out var v) && v.OfType<bool>().Any() ? v.OfType<bool>().First() : null;
    }

    // Enteros y enums (0x21, 0x23), booleanos (0x22) y textos (0x30–0x4F: keyword, uri, mimeMediaType…).
    internal static IppRespuesta ParseResponse(int http, byte[] data)
    {
        var valores = new Dictionary<string, List<object>>();
        if (data.Length < 8) return new IppRespuesta(http, null, valores);

        var estado = (data[2] << 8) | data[3]; // status-code, tras la versión
        var i = 8; // skip 8-byte header (version 2 + status-code 2 + request-id 4)
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

            object? valor = tag switch
            {
                0x21 or 0x23 when valueLen == 4 =>
                    (data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3],
                0x22 when valueLen == 1 => data[i] != 0,
                // resolution: x, y y unidades (3 = por pulgada, 4 = por centímetro); se guarda x en ppp.
                0x32 when valueLen == 9 =>
                    (int)(((data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3]) * (data[i + 8] == 4 ? 2.54 : 1)),
                >= 0x30 and <= 0x4F => Encoding.UTF8.GetString(data, i, valueLen),
                _ => null
            };
            if (valor is not null)
            {
                if (!valores.TryGetValue(name, out var lista))
                    valores[name] = lista = [];
                lista.Add(valor);
            }

            i += valueLen;
        }

        return new IppRespuesta(http, estado, valores);
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
