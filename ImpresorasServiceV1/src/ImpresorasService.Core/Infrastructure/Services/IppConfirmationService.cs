using System.Net.Http.Headers;
using System.Text;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain;
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
                // Recortado a last_error_code (varchar 60): un motivo de fabricante más largo tumbaría
                // el lote entero del watchdog en cada ciclo.
                var errorCode = reasons?.Split(',').FirstOrDefault()?.Trim() is { } motivo
                    ? motivo[..Math.Min(motivo.Length, 60)]
                    : "IPP_PRINTER_STOPPED";
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

    public async Task<PrintSpoolResult?> PrintPdfAsync(string printerHost, byte[] pdf, string jobName, bool comoImagen, CancellationToken ct)
    {
        var ip = ParseHost(printerHost);
        if (ip is null)
            return null;

        // 1. ¿Acepta trabajos, y en qué formato? Hasta que la impresora acepte el trabajo no ha salido
        // nada: cualquier fallo vuelve como NoEnviado, con su motivo, y se puede probar por la cola.
        (string Ruta, IppRespuesta Respuesta)? atributos;
        try
        {
            atributos = await GetPrinterAttributesAsync(
                ip, ["document-format-supported", "printer-is-accepting-jobs",
                    "pwg-raster-document-type-supported", "pwg-raster-document-resolution-supported"], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogInformation("IPP: {Host} no responde ({Error}).", ip, ex.Message);
            return NoEnviado("IPP_NO_RESPONSE", $"La impresora no responde por IPP: {ex.Message}", transitorio: true);
        }
        if (atributos is null)
            return NoEnviado("IPP_NO_RESPONSE", "La impresora no responde por IPP.", transitorio: true);
        var (ruta, impresora) = atributos.Value;
        if (impresora.Booleano("printer-is-accepting-jobs") == false)
            return NoEnviado("IPP_PRINTER_BUSY", "La impresora no acepta trabajos ahora.", transitorio: true);

        // PDF si lo acepta; si no, imagen PWG (IPP Everywhere), que dibuja el servicio.
        var formatos = impresora.Textos("document-format-supported").ToHashSet();
        var tipos = impresora.Textos("pwg-raster-document-type-supported").ToHashSet();
        var aceptaPdf = formatos.Contains("application/pdf");
        var aceptaImagen = formatos.Contains("image/pwg-raster") && (tipos.Contains("sgray_8") || tipos.Contains("srgb_8"));
        if (!aceptaPdf && !aceptaImagen)
            return NoEnviado("IPP_FORMAT_NOT_SUPPORTED", "La impresora no acepta PDF ni imagen PWG por IPP.", transitorio: false);

        var porPdf = aceptaPdf && !(comoImagen && aceptaImagen);
        var resultado = porPdf ? await EnviarAsync("application/pdf", pdf) : await ComoImagenAsync();
        // 2. Si dice que no sabe leer el PDF, otra vez como imagen, que la dibuja el servicio.
        if (porPdf && aceptaImagen && resultado is { SinEnviar: true, ErrorCode: "IPP_DOCUMENT_ERROR" })
        {
            _logger.LogInformation("IPP: {Host} no sabe leer el PDF; se manda como imagen.", ip);
            resultado = await ComoImagenAsync();
        }
        return resultado;

        async Task<PrintSpoolResult> ComoImagenAsync()
        {
            var resoluciones = impresora.Enteros("pwg-raster-document-resolution-supported").ToList();
            var dpi = resoluciones.Contains(300) || resoluciones.Count == 0 ? 300 : resoluciones.Min();
            byte[] imagen;
            try
            {
                imagen = PwgRaster.DesdePdf(pdf, dpi, color: !tipos.Contains("sgray_8"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IPP: no se pudo convertir el PDF a imagen para {Host}.", ip);
                return NoEnviado("PDF_INVALID", "No se pudo convertir el PDF a imagen: está dañado o no es un PDF.", transitorio: false);
            }
            return await EnviarAsync("image/pwg-raster", imagen);
        }

        async Task<PrintSpoolResult> EnviarAsync(string formato, byte[] documento)
        {
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
                return NoEnviado("IPP_NO_RESPONSE", "La impresora rechaza la conexión por IPP.", transitorio: true);
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
                // La impresora no aceptó el trabajo o falló con él: Rechazo dice si pudo salir papel.
                var rechazo = Rechazo(respuesta.Http, respuesta.Estado);
                _logger.LogInformation("IPP: {Host} rechaza el trabajo: {Codigo} {Mensaje}", ip, rechazo.ErrorCode, rechazo.ErrorMessage);
                return rechazo;
            }

            var jobId = respuesta.Entero("job-id");
            _logger.LogInformation("IPP: {Host} aceptó el trabajo {JobId}.", ip, jobId);
            return new PrintSpoolResult(true, null, null, false,
                Detalle: $"La impresora aceptó el trabajo por IP ({ip}, trabajo {jobId?.ToString() ?? "sin número"}"
                    + (formato == "application/pdf" ? ")" : ", como imagen)"),
                IppJobId: jobId);
        }
    }

    private static PrintSpoolResult NoEnviado(string codigo, string mensaje, bool transitorio) =>
        new(false, codigo, mensaje, transitorio, SinEnviar: true);

    /// <summary>
    /// La respuesta de error de la impresora al Print-Job (HTTP y status-code de IPP, RFC 8011 §B),
    /// según si ha podido salir papel (decisión D4 del 27/10/2026, docs/auditoria-2026-10-26.md).
    /// Lo que rechaza antes de leer el documento no ha salido: se puede insistir (como imagen, por la
    /// cola, más tarde). Lo que falla con el documento a medias, o no conocemos, pudo sacar parte: no
    /// se reenvía y decide una persona.
    /// </summary>
    internal static PrintSpoolResult Rechazo(int http, int? estado)
    {
        var detalle = $"(HTTP {http}, IPP 0x{estado ?? 0:X4})";
        return (http, estado) switch
        {
            (401 or 403, _) or (_, 0x0401 or 0x0402 or 0x0403) =>
                NoEnviado("IPP_NOT_AUTHORIZED", $"La impresora pide usuario o no deja imprimir por IPP {detalle}.", transitorio: false),
            (413, _) or (_, 0x0408) =>
                NoEnviado("IPP_TOO_LARGE", $"El documento es demasiado grande para la impresora {detalle}.", transitorio: false),
            (_, 0x040A or 0x040F) =>
                NoEnviado("IPP_DOCUMENT_ERROR", $"La impresora no admite el documento {detalle}.", transitorio: false),
            (503, _) or (_, 0x0502 or 0x0506 or 0x0507) =>
                NoEnviado("IPP_PRINTER_BUSY", $"La impresora está ocupada o no acepta trabajos ahora {detalle}.", transitorio: true),
            (_, 0x0501 or 0x0503) =>
                NoEnviado("IPP_REJECTED", $"La impresora no admite imprimir por IPP {detalle}.", transitorio: false),
            // HTTP 4xx sin respuesta IPP (405, 415…): el servidor web de la impresora lo rechaza antes de leerlo.
            (>= 400 and < 500, null) or (_, >= 0x0400 and <= 0x040F) =>
                NoEnviado("IPP_REJECTED", $"La impresora rechazó el trabajo {detalle}.", transitorio: true),
            (_, 0x0508) =>
                new(false, "IPP_JOB_CANCELED", $"Lo cancelaron en la impresora mientras salía {detalle}.", false),
            // 0x0410-0x0412 (documento a medias), 0x0500/0x0504/0x0505 (avería imprimiendo), HTTP 500…
            _ => new(false, "IPP_ERROR_WHILE_PRINTING",
                $"La impresora falló con el trabajo a medias {detalle}: puede haber sacado parte.", false),
        };
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
                var r = await PostAsync(ip, ruta, BuildJobRequest(ip, ruta, jobId), linked.Token);
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

    public async Task<CancelacionEnImpresora> CancelJobAsync(string printerHost, int jobId, string jobName, CancellationToken ct)
    {
        var ip = ParseHost(printerHost);
        if (ip is null)
            return CancelacionEnImpresora.SinRespuesta;

        try
        {
            var respondio = false;
            foreach (var ruta in Rutas)
            {
                using var consulta = CancellationTokenSource.CreateLinkedTokenSource(ct);
                consulta.CancelAfter(_options.Value.IppTimeoutMs);
                var r = await PostAsync(ip, ruta, BuildJobRequest(ip, ruta, jobId), consulta.Token);
                respondio |= r.Http == 200;
                if (r.Http != 200 || r.Estado is null or >= 0x0100)
                    continue; // ruta equivocada o trabajo olvidado (client-error-not-found)

                // El mismo nombre que en el Print-Job; si no, ese número es ahora otro documento. Sin
                // nombre no se puede comprobar: no se toca (pendiente de ver qué hace la Brother, V10).
                var nombre = r.Textos("job-name").FirstOrDefault();
                if (nombre is null)
                    return CancelacionEnImpresora.SinNombre;
                if (nombre != NombreDeTrabajo(jobName))
                    return CancelacionEnImpresora.NoEsNuestro;
                switch (r.Entero("job-state"))
                {
                    case 9: return CancelacionEnImpresora.YaSalio;
                    case 7 or 8: return CancelacionEnImpresora.YaNoSaldra;
                }

                using var cancelar = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cancelar.CancelAfter(_options.Value.IppTimeoutMs);
                var c = await PostAsync(ip, ruta, BuildJobRequest(ip, ruta, jobId, cancelar: true), cancelar.Token);
                return c.Http == 200 && c.Estado is < 0x0100
                    ? CancelacionEnImpresora.Cancelado
                    : CancelacionEnImpresora.NoDeja;
            }
            return respondio ? CancelacionEnImpresora.NoLoTiene : CancelacionEnImpresora.SinRespuesta;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "IPP: no se pudo cancelar el trabajo {JobId} en {Host}.", jobId, ip);
            return CancelacionEnImpresora.SinRespuesta;
        }
    }

    // Como mucho 100 caracteres, como se manda en el Print-Job.
    internal static string NombreDeTrabajo(string jobName) => jobName.Length > 100 ? jobName[..100] : jobName;

    internal static IppQueryResult? EstadoDelTrabajo(IppRespuesta r)
    {
        var razones = r.Textos("job-state-reasons").Where(x => x is not "none" and not "job-completed-successfully").ToList();
        var detalle = razones.Count > 0 ? TranslateReasons(string.Join(", ", razones)) : null;
        // Abortado porque no supo leer el documento (formato, compresión, documento ilegible).
        var ilegible = razones.Any(x => x.StartsWith("document-", StringComparison.Ordinal)
            || x is "compression-error" or "unsupported-document-format");
        return r.Entero("job-state") switch
        {
            9 when razones.Contains("job-completed-with-errors") =>
                new IppQueryResult(IppOutcome.CompletedWithErrors, "IPP_COMPLETED_WITH_ERRORS",
                    "La impresora dio el trabajo por terminado, pero con errores: comprueba que salió bien."),
            9 => new IppQueryResult(IppOutcome.PrinterIdle),
            8 when ilegible => new IppQueryResult(IppOutcome.JobAborted, "IPP_DOCUMENT_ERROR",
                "La impresora no pudo leer el documento" + (detalle is null ? "." : $": {detalle}")),
            // En cola detrás de una impresora parada: es «parada», no «procesando» (aviso y tope de D3).
            3 or 4 or 5 when razones.Contains("printer-stopped") =>
                new IppQueryResult(IppOutcome.PrinterStopped, "IPP_JOB_STOPPED", detalle),
            3 or 4 or 5 => new IppQueryResult(IppOutcome.PrinterProcessing),
            6 => new IppQueryResult(IppOutcome.PrinterStopped, "IPP_JOB_STOPPED", detalle ?? TranslateReasons(null)),
            7 => new IppQueryResult(IppOutcome.JobAborted, "IPP_JOB_CANCELED",
                "El trabajo se canceló en la impresora" + (detalle is null ? "." : $": {detalle}")),
            8 => new IppQueryResult(IppOutcome.JobAborted, "IPP_JOB_ABORTED",
                "La impresora abortó el trabajo" + (detalle is null ? "." : $": {detalle}")),
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

    // Get-Job-Attributes o, con cancelar, Cancel-Job de un trabajo que mandó el servicio. Con el mismo
    // requesting-user-name que el Print-Job: hay impresoras que solo dejan ver o cancelar un trabajo
    // a quien lo mandó.
    internal static byte[] BuildJobRequest(string ip, string ruta, int jobId, bool cancelar = false)
    {
        var ms = new MemoryStream();

        ms.WriteByte(0x01); ms.WriteByte(0x01);   // version 1.1
        ms.WriteByte(0x00); ms.WriteByte(cancelar ? (byte)0x08 : (byte)0x09); // Cancel-Job o Get-Job-Attributes
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
        WriteAttr(ms, 0x42, "requesting-user-name", "ImpresorasService");
        if (!cancelar)
        {
            WriteAttr(ms, 0x44, "requested-attributes", "job-state");
            WriteAttr(ms, 0x44, "", "job-state-reasons");
            WriteAttr(ms, 0x44, "", "job-name"); // para comprobar que el número sigue siendo el nuestro
        }

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
        WriteAttr(ms, 0x42, "job-name", NombreDeTrabajo(jobName));
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

    // Motivos de IPP en castellano (catálogo de ErroresTrabajo); los que no conoce, tal cual.
    private static string TranslateReasons(string? reasons)
    {
        if (string.IsNullOrWhiteSpace(reasons))
            return "La impresora ha reportado un error desconocido.";

        var parts = reasons.Split(',')
            .Select(r => r.Trim())
            .Where(r => r is not "" and not "none")
            .Select(r => ErroresTrabajo.MotivoIpp(r) ?? $"Error de impresora: {r}")
            .ToList();

        return parts.Count > 0
            ? string.Join(". ", parts) + "."
            : "La impresora ha reportado un error.";
    }

    public void Dispose() => _http.Dispose();
}
