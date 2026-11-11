using System.Buffers.Binary;
using System.Text;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// Se imprime primero por IP (IPP) y la cola de Windows es la alternativa (decisión del usuario,
/// 26/10/2026). Lo que no puede pasar: que un envío por IP cortado a medias se repita por la cola.
/// </summary>
public sealed class ImpresionPorIpTests
{
    private static readonly PrintSpoolResult Aceptado =
        new(true, null, null, false, Detalle: "La impresora aceptó el trabajo por IP (10.0.0.9, trabajo 7)", IppJobId: 7);
    // La impresora no aceptó el trabajo: no salió nada.
    private static readonly PrintSpoolResult Ocupada =
        new(false, "IPP_PRINTER_BUSY", "La impresora está ocupada.", true, SinEnviar: true);

    [Fact]
    public async Task Con_IP_que_acepta_PDF_sale_por_IP_y_no_toca_la_cola()
    {
        var (estado, ipp, cola, mensaje, ippJobId, _) = await ImprimirAsync(host: "10.0.0.9", queue: "COLA", porIp: Aceptado);

        Assert.Equal(PrintJobStatus.SpoolAccepted, estado);
        Assert.Equal(1, ipp.Llamadas);
        Assert.Equal(0, cola.CallCount);
        Assert.Contains("por IP", mensaje);
        Assert.Equal(7, ippJobId); // con él, el watchdog confirma ese trabajo
    }

    [Fact]
    public async Task Si_por_IP_no_se_puede_va_por_la_cola()
    {
        var (estado, ipp, cola, _, ippJobId, _) = await ImprimirAsync(host: "10.0.0.9", queue: "COLA", porIp: Ocupada);

        Assert.Equal(PrintJobStatus.SpoolAccepted, estado);
        Assert.Equal(1, ipp.Llamadas);
        Assert.Equal("COLA", cola.LastQueueName);
        Assert.Null(ippJobId);
    }

    // Get-Job-Attributes (0x0009) y Cancel-Job (0x0008): el trabajo por su número y a nombre del servicio,
    // como el Print-Job, porque hay impresoras que solo dejan verlo o cancelarlo a quien lo mandó. La
    // consulta pide también job-name: antes de cancelar se comprueba que el número sigue siendo el nuestro.
    [Theory]
    [InlineData(false, 0x0009)]
    [InlineData(true, 0x0008)]
    public void Consultar_o_cancelar_un_trabajo_va_por_su_numero(bool cancelar, int operacion)
    {
        var peticion = IppConfirmationService.BuildJobRequest("10.0.0.9", "/ipp/print", 258, cancelar);

        Assert.Equal(operacion, BinaryPrimitives.ReadUInt16BigEndian(peticion.AsSpan(2)));
        Assert.Equal(!cancelar, Encoding.UTF8.GetString(peticion).Contains("job-name"));
        var nombre = Encoding.UTF8.GetBytes("job-id");
        var i = peticion.AsSpan().IndexOf(nombre);
        Assert.Equal(0x21, peticion[i - 3]); // integer
        Assert.Equal(258, BinaryPrimitives.ReadInt32BigEndian(peticion.AsSpan(i + nombre.Length + 2)));
        Assert.Contains("requesting-user-name", Encoding.UTF8.GetString(peticion));
    }

    [Theory]
    [InlineData(9, IppOutcome.PrinterIdle)]
    [InlineData(5, IppOutcome.PrinterProcessing)]
    [InlineData(3, IppOutcome.PrinterProcessing)]
    [InlineData(6, IppOutcome.PrinterStopped)]
    [InlineData(7, IppOutcome.JobAborted)]
    [InlineData(8, IppOutcome.JobAborted)]
    public void El_estado_del_trabajo_decide_la_confirmacion(byte jobState, IppOutcome esperado)
    {
        var r = IppConfirmationService.ParseResponse(200, Respuesta(0x0000,
            (0x23, "job-state", [0, 0, 0, jobState]),
            (0x44, "job-state-reasons", Encoding.UTF8.GetBytes("media-jam"))));

        var estado = IppConfirmationService.EstadoDelTrabajo(r);

        Assert.Equal(esperado, estado!.Outcome);
        if (jobState == 8) Assert.Contains("Atasco de papel", estado.ErrorMessage);
    }

    [Theory]
    [InlineData(9, "job-completed-with-errors", IppOutcome.CompletedWithErrors, "IPP_COMPLETED_WITH_ERRORS")]
    [InlineData(9, "job-completed-with-warnings", IppOutcome.PrinterIdle, null)]
    [InlineData(8, "document-format-error", IppOutcome.JobAborted, "IPP_DOCUMENT_ERROR")]
    [InlineData(8, "document-unprintable-error", IppOutcome.JobAborted, "IPP_DOCUMENT_ERROR")]
    [InlineData(8, "aborted-by-system", IppOutcome.JobAborted, "IPP_JOB_ABORTED")]
    [InlineData(3, "printer-stopped", IppOutcome.PrinterStopped, "IPP_JOB_STOPPED")] // en cola tras una impresora parada
    public void Los_motivos_del_trabajo_afinan_el_resultado(byte jobState, string motivo, IppOutcome esperado, string? codigo)
    {
        var r = IppConfirmationService.ParseResponse(200, Respuesta(0x0000,
            (0x23, "job-state", [0, 0, 0, jobState]),
            (0x44, "job-state-reasons", Encoding.UTF8.GetBytes(motivo))));

        var estado = IppConfirmationService.EstadoDelTrabajo(r)!;

        Assert.Equal(esperado, estado.Outcome);
        Assert.Equal(codigo, estado.ErrorCode);
    }

    // Decisión D4 (docs/auditoria-2026-10-26.md, 2.2): lo rechazado antes de leer el documento no ha
    // salido (SinEnviar); lo que falla a medias, o no conocemos, pudo sacar papel.
    [Theory]
    [InlineData(401, null, "IPP_NOT_AUTHORIZED", false, true)]
    [InlineData(200, 0x0403, "IPP_NOT_AUTHORIZED", false, true)]
    [InlineData(413, null, "IPP_TOO_LARGE", false, true)]
    [InlineData(200, 0x0408, "IPP_TOO_LARGE", false, true)]
    [InlineData(200, 0x040A, "IPP_DOCUMENT_ERROR", false, true)]
    [InlineData(200, 0x0507, "IPP_PRINTER_BUSY", true, true)]
    [InlineData(503, null, "IPP_PRINTER_BUSY", true, true)]
    [InlineData(200, 0x0400, "IPP_REJECTED", true, true)]
    [InlineData(200, 0x0501, "IPP_REJECTED", false, true)]
    [InlineData(415, null, "IPP_REJECTED", true, true)]
    [InlineData(200, 0x0411, "IPP_ERROR_WHILE_PRINTING", false, false)]
    [InlineData(200, 0x0504, "IPP_ERROR_WHILE_PRINTING", false, false)]
    [InlineData(200, 0x0505, "IPP_ERROR_WHILE_PRINTING", false, false)]
    [InlineData(500, null, "IPP_ERROR_WHILE_PRINTING", false, false)]
    [InlineData(200, 0x0508, "IPP_JOB_CANCELED", false, false)]
    public void Un_rechazo_dice_por_que_y_si_pudo_salir_papel(int http, int? estado, string codigo, bool transitorio, bool sinEnviar)
    {
        var r = IppConfirmationService.Rechazo(http, estado);

        Assert.Equal((codigo, transitorio, sinEnviar), (r.ErrorCode, r.IsTransient, r.SinEnviar));
    }

    [Theory]
    [InlineData(0x0504, PrintJobStatus.PrintedUnknown)] // avería a medias: pudo salir parte
    [InlineData(0x0508, PrintJobStatus.ErrorFinal)]     // lo cancelaron en la impresora
    public async Task Si_pudo_salir_papel_no_se_reenvia_por_la_cola(int estadoIpp, PrintJobStatus esperado)
    {
        var (estado, _, cola, _, _, _) = await ImprimirAsync(host: "10.0.0.9", queue: "COLA",
            porIp: IppConfirmationService.Rechazo(200, estadoIpp));

        Assert.Equal(esperado, estado);
        Assert.Equal(0, cola.CallCount);
    }

    [Fact]
    public async Task Lo_que_la_impresora_aborto_por_el_documento_se_reintenta_como_imagen()
    {
        var (estado, ipp, _, _, _, _) = await ImprimirAsync(host: "10.0.0.9", queue: null, porIp: Aceptado, abortadoPorDocumento: true);

        Assert.Equal(PrintJobStatus.SpoolAccepted, estado);
        Assert.True(ipp.ComoImagen);
    }

    [Fact]
    public async Task Parar_el_servicio_a_mitad_no_pierde_lo_que_la_impresora_acepto()
    {
        using var parada = new CancellationTokenSource();

        var (estado, _, _, _, ippJobId, _) = await ImprimirAsync(host: "10.0.0.9", queue: null, porIp: Aceptado, parada: parada);

        Assert.True(parada.IsCancellationRequested);
        Assert.Equal(PrintJobStatus.SpoolAccepted, estado);
        Assert.Equal(7, ippJobId);
    }

    [Fact]
    public async Task Un_envio_por_IP_cortado_no_se_repite_por_la_cola()
    {
        var cortado = new PrintSpoolResult(false, "NET_TIMEOUT", "El envío por IP se cortó sin conocerse el resultado.", false);

        var (estado, _, cola, _, _, _) = await ImprimirAsync(host: "10.0.0.9", queue: "COLA", porIp: cortado);

        Assert.Equal(PrintJobStatus.PrintedUnknown, estado);
        Assert.Equal(0, cola.CallCount);
    }

    [Fact]
    public async Task Sin_IP_va_directo_por_la_cola()
    {
        var (estado, ipp, cola, _, _, _) = await ImprimirAsync(host: null, queue: "COLA", porIp: Aceptado);

        Assert.Equal(PrintJobStatus.SpoolAccepted, estado);
        Assert.Equal(0, ipp.Llamadas);
        Assert.Equal(1, cola.CallCount);
    }

    [Fact]
    public async Task Solo_IP_y_la_impresora_no_puede_se_reintenta_con_su_motivo()
    {
        var (estado, _, cola, _, _, codigo) = await ImprimirAsync(host: "10.0.0.9", queue: null, porIp: Ocupada);

        Assert.Equal(PrintJobStatus.RetryScheduled, estado);
        Assert.Equal("IPP_PRINTER_BUSY", codigo);
        Assert.Equal(0, cola.CallCount);
    }

    [Fact]
    public async Task Sin_IP_valida_ni_cola_no_se_puede_imprimir()
    {
        var (estado, _, _, _, _, codigo) = await ImprimirAsync(host: "10.0.0.9", queue: null, porIp: null);

        Assert.Equal(PrintJobStatus.ErrorFinal, estado);
        Assert.Equal("NO_PRINT_ROUTE", codigo);
    }

    // Solo por IP (Linux, 02/11/2026): la cola de la ficha no se usa; queda el motivo de IPP.
    [Fact]
    public async Task Solo_IP_no_recurre_a_la_cola_aunque_la_ficha_la_tenga()
    {
        var (estado, ipp, _, _, _, codigo) = await ImprimirAsync(host: "10.0.0.9", queue: "COLA", porIp: Ocupada, soloIp: true);

        Assert.Equal(PrintJobStatus.RetryScheduled, estado);
        Assert.Equal("IPP_PRINTER_BUSY", codigo);
        Assert.Equal(1, ipp.Llamadas);
    }

    [Fact]
    public async Task Solo_IP_sale_por_IP()
    {
        var (estado, _, _, _, ippJobId, _) = await ImprimirAsync(host: "10.0.0.9", queue: "COLA", porIp: Aceptado, soloIp: true);

        Assert.Equal(PrintJobStatus.SpoolAccepted, estado);
        Assert.Equal(7, ippJobId);
    }

    // Production en Linux: SoloIp arranca (papel real); con UseRealSpooler a la vez, no.
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Solo_IP_arranca_en_Production(bool soloIp, bool useRealSpooler, bool arranca)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PrintQueue"] = "Host=localhost;Database=x;Username=x",
            ["PrintExecution:SoloIp"] = soloIp.ToString(),
            ["PrintExecution:UseRealSpooler"] = useRealSpooler.ToString(),
        }).Build();

        var registrar = () => new ServiceCollection().AddInfrastructure(config, "Production").BuildServiceProvider();

        if (arranca)
            Assert.IsType<SinColaPrintSpooler>(registrar().GetRequiredService<IPrinterSpooler>());
        else
            Assert.Throws<InvalidOperationException>(registrar);
    }

    [Fact]
    public async Task En_simulacion_tampoco_sale_por_IP()
    {
        var (estado, ipp, _, _, _, _) = await ImprimirAsync(host: "10.0.0.9", queue: null, porIp: Aceptado, simulacion: true);

        Assert.Equal(PrintJobStatus.SpoolAccepted, estado);
        Assert.Equal(0, ipp.Llamadas);
    }

    // Sin IP ni cola UNC el monitor anota HOST_NOT_CONFIGURED: no es «no alcanzable».
    [Theory]
    [InlineData("HOST_NOT_CONFIGURED", PrintJobStatus.SpoolAccepted)]
    [InlineData("TCP_TIMEOUT", PrintJobStatus.RetryScheduled)]
    public async Task Una_impresora_con_solo_cola_local_imprime(string errorConexion, PrintJobStatus esperado)
    {
        var (estado, _, cola, _, _, _) = await ImprimirAsync(host: null, queue: "Brother local", porIp: null, errorConexion: errorConexion);

        Assert.Equal(esperado, estado);
        Assert.Equal(esperado == PrintJobStatus.SpoolAccepted ? 1 : 0, cola.CallCount);
    }

    [Fact]
    public void Print_Job_lleva_el_PDF_como_application_pdf_detras_de_los_atributos()
    {
        byte[] pdf = [0x25, 0x50, 0x44, 0x46, 0x2D];

        var peticion = IppConfirmationService.BuildPrintJobRequest("10.0.0.9", "/ipp/print", "ALBARAN A-1", pdf);

        Assert.Equal(0x0002, BinaryPrimitives.ReadUInt16BigEndian(peticion.AsSpan(2))); // Print-Job
        var texto = Encoding.UTF8.GetString(peticion);
        Assert.Contains("ipp://10.0.0.9:631/ipp/print", texto);
        Assert.Contains("application/pdf", texto);
        Assert.Equal([0x03, .. pdf], peticion[^(pdf.Length + 1)..]); // fin de atributos y el documento
    }

    [Fact]
    public void Lee_estado_formatos_aceptados_y_numero_de_trabajo()
    {
        var respuesta = Respuesta(0x0000,
            (0x44, "document-format-supported", Encoding.UTF8.GetBytes("image/urf")),
            (0x44, "", Encoding.UTF8.GetBytes("application/pdf")),
            (0x22, "printer-is-accepting-jobs", [1]),
            (0x21, "job-id", [0, 0, 0, 42]));

        var r = IppConfirmationService.ParseResponse(200, respuesta);

        Assert.Equal(0, r.Estado);
        Assert.Equal(["image/urf", "application/pdf"], r.Textos("document-format-supported"));
        Assert.True(r.Booleano("printer-is-accepting-jobs"));
        Assert.Equal(42, r.Entero("job-id"));
    }

    // ERP_SERCA docs/72, R0 (10/11/2026): lo que contestan la Canon imageFORCE 1643 (80 % del parque) y
    // la Brother MFC-L2710DW a Get-Printer-Attributes, recortado a lo que se guarda.
    [Fact]
    public void Estado_de_la_Canon_sin_mensaje_y_con_toner_bajo()
    {
        var r = IppConfirmationService.ParseResponse(200, Respuesta(0x0000,
            (0x23, "printer-state", Entero(3)),
            (0x44, "printer-state-reasons", Encoding.UTF8.GetBytes("none")),
            (0x41, "printer-state-message", Encoding.UTF8.GetBytes("none")),
            (0x21, "queued-job-count", Entero(0)),
            (0x42, "marker-names", Encoding.UTF8.GetBytes("Canon Toner T06 Black Toner")),
            (0x42, "marker-colors", Encoding.UTF8.GetBytes("#000000")),
            (0x44, "marker-types", Encoding.UTF8.GetBytes("toner")),
            (0x21, "marker-levels", Entero(14)),
            (0x21, "marker-low-levels", Entero(7)),
            (0x41, "printer-make-and-model", Encoding.UTF8.GetBytes("Canon imageFORCE 1643")),
            (0x45, "printer-more-info", Encoding.UTF8.GetBytes("http://192.42.170.6:8000/rps/airprint"))));

        var e = IppConfirmationService.LeerEstado(r);

        Assert.Equal(3, e.State);
        Assert.Null(e.Reasons);
        Assert.Null(e.Message); // «none» no es un mensaje
        Assert.Equal(0, e.QueuedJobs);
        Assert.Equal([new IppConsumible("Canon Toner T06 Black Toner", "#000000", "toner", 14, 7)], e.Consumibles);
        Assert.Equal("Canon imageFORCE 1643", e.MakeAndModel);
        Assert.Equal("http://192.42.170.6:8000/rps/airprint", e.MoreInfoUrl);
    }

    [Fact]
    public void Estado_de_la_Brother_con_textos_con_idioma_y_aviso_del_panel()
    {
        var r = IppConfirmationService.ParseResponse(200, Respuesta(0x0000,
            (0x23, "printer-state", Entero(3)),
            (0x44, "printer-state-reasons", Encoding.UTF8.GetBytes("opc-life-over-warning")),
            (0x35, "printer-alert-description", ConIdioma("es", "Cambie tambor")),
            (0x35, "", ConIdioma("es", "Espera")),
            (0x36, "marker-names", ConIdioma("en", "BK")),
            (0x21, "marker-levels", Entero(60)),
            (0x21, "marker-low-levels", Entero(9)),
            (0x35, "printer-make-and-model", ConIdioma("en", "Brother MFC-L2710DW series"))));

        var e = IppConfirmationService.LeerEstado(r);

        Assert.Equal("opc-life-over-warning", e.Reasons);
        Assert.Equal("Cambie tambor, Espera", e.Message);
        Assert.Equal("Brother MFC-L2710DW series", e.MakeAndModel);
        Assert.Equal([new IppConsumible("BK", null, null, 60, 9)], e.Consumibles);
    }

    [Fact]
    public void Guardar_el_estado_y_borrarlo()
    {
        var p = new Printer();
        var leido = DateTimeOffset.UtcNow;
        var e = new IppEstadoImpresora(5, "media-empty-error", null, 2,
            [new IppConsumible("BK", null, "toner", 14, 7)], "Canon imageFORCE 1643", null);

        IppEstadoImpresora.AplicarA(p, e, leido);
        Assert.Equal(5, p.IppState);
        Assert.Equal("media-empty-error", p.IppStateReasons);
        Assert.Equal(2, p.IppQueuedJobs);
        Assert.Equal("""[{"nombre":"BK","color":null,"tipo":"toner","nivel":14,"bajo":7}]""", p.IppMarkersJson);
        Assert.Equal(leido, p.IppCheckedAtUtc);

        IppEstadoImpresora.AplicarA(p, null, leido);
        Assert.Null(p.IppState);
        Assert.Null(p.IppMarkersJson);
        Assert.Null(p.IppMakeAndModel);
        Assert.Null(p.IppCheckedAtUtc);
    }

    // R2: gravedad por el sufijo del motivo; tóner por los umbrales de Umbrales (por defecto 20 y 10 %).
    [Theory]
    [InlineData(3, null, 83, "healthy", "Lista")]
    [InlineData(4, null, 83, "healthy", "Imprimiendo")]
    [InlineData(3, null, 21, "healthy", "Lista")]
    [InlineData(3, null, 20, "warning", "BK al 20 %")]                 // en el umbral: ya avisa
    [InlineData(3, null, 14, "warning", "BK al 14 %")]                 // TRASPASOS el 10/11/2026
    [InlineData(3, null, 10, "critical", "BK al 10 %")]
    [InlineData(3, null, 6, "critical", "BK al 6 %")]
    [InlineData(3, null, -3, "healthy", "Lista")]                      // «queda algo»: no es un %
    [InlineData(3, "opc-life-over-warning", 60, "warning", "El cartucho fotoconductor ha llegado al final de su vida útil")]
    [InlineData(5, "media-empty-error", 83, "critical", "Sin papel en la bandeja")]
    [InlineData(3, "media-jam", 83, "critical", "Atasco de papel")]     // sin sufijo: error (RFC 8011)
    [InlineData(3, "toner-low-report", 83, "healthy", "Lista")]        // -report: nada
    [InlineData(5, null, 83, "critical", "La impresora está detenida")]
    [InlineData(3, "media-low-warning, door-open-error", 6, "critical", "Papel casi agotado. La puerta de la impresora está abierta. BK al 6 %")]
    public void Describir_el_estado(int estado, string? motivos, int toner, string gravedad, string texto)
    {
        var p = new Printer();
        var ahora = DateTimeOffset.UtcNow;
        IppEstadoImpresora.AplicarA(p, new IppEstadoImpresora(estado, motivos, null, 0,
            [new IppConsumible("BK", null, "toner", toner, 7)], null, null), ahora.AddSeconds(-20));

        Assert.Equal((gravedad, texto), IppEstadoImpresora.Describir(p, ahora, UmbralToner.Default));
    }

    [Fact]
    public void Describir_el_toner_con_otros_umbrales()
    {
        var p = new Printer();
        var ahora = DateTimeOffset.UtcNow;
        IppEstadoImpresora.AplicarA(p, new IppEstadoImpresora(3, null, null, 0,
            [new IppConsumible("BK", null, "toner", 14, 7)], null, null), ahora);

        Assert.Equal("healthy", IppEstadoImpresora.Describir(p, ahora, new UmbralToner(10, 5)).Gravedad);
        Assert.Equal("critical", IppEstadoImpresora.Describir(p, ahora, new UmbralToner(30, 15)).Gravedad);
    }

    [Fact]
    public void Describir_sin_lectura_o_con_lectura_vieja_no_afirma_nada()
    {
        var p = new Printer();
        var ahora = DateTimeOffset.UtcNow;
        Assert.Equal(("neutral", null), IppEstadoImpresora.Describir(p, ahora, UmbralToner.Default));

        IppEstadoImpresora.AplicarA(p, new IppEstadoImpresora(5, "media-jam-error", null, 0, [], null, null),
            ahora - IppEstadoImpresora.LecturaVieja - TimeSpan.FromSeconds(1));
        Assert.Equal(("neutral", "Sin lectura reciente"), IppEstadoImpresora.Describir(p, ahora, UmbralToner.Default));
    }

    private static byte[] Entero(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static byte[] ConIdioma(string idioma, string texto)
    {
        var i = Encoding.UTF8.GetBytes(idioma);
        var t = Encoding.UTF8.GetBytes(texto);
        return [(byte)(i.Length >> 8), (byte)i.Length, .. i, (byte)(t.Length >> 8), (byte)t.Length, .. t];
    }

    private static byte[] Respuesta(ushort estado, params (byte Tag, string Nombre, byte[] Valor)[] atributos)
    {
        var ms = new MemoryStream();
        ms.Write([0x01, 0x01, (byte)(estado >> 8), (byte)estado, 0, 0, 0, 1, 0x04]); // printer-attributes
        foreach (var (tag, nombre, valor) in atributos)
        {
            var n = Encoding.UTF8.GetBytes(nombre);
            ms.WriteByte(tag);
            ms.Write([(byte)(n.Length >> 8), (byte)n.Length]); ms.Write(n);
            ms.Write([(byte)(valor.Length >> 8), (byte)valor.Length]); ms.Write(valor);
        }
        ms.WriteByte(0x03);
        return ms.ToArray();
    }

    private static async Task<(PrintJobStatus Estado, IppFalso Ipp, FakeSpooler Cola, string Mensaje, int? IppJobId, string? Codigo)> ImprimirAsync(
        string? host, string? queue, PrintSpoolResult? porIp, bool simulacion = false, string? errorConexion = null,
        bool abortadoPorDocumento = false, CancellationTokenSource? parada = null, bool soloIp = false)
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;
        var now = DateTimeOffset.UtcNow;
        db.Stores.Add(new Store { StoreId = 1, Name = "T1", IsActive = true });
        db.Printers.Add(new Printer
        {
            PrinterId = 10, PrinterName = "P", SpoolQueue = queue, Host = host, StoreId = 1,
            IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now,
            LastConnectionOk = errorConexion is null ? null : false,
            LastConnectionCheckAtUtc = errorConexion is null ? null : now,
            LastConnectionError = errorConexion
        });
        var jobId = Guid.NewGuid();
        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId, SourceSystem = "TEST", ExternalJobId = "A-1", StoreId = 1, DocumentType = "ALBARAN",
            Channel = "DEFAULT", PdfBlob = [0x25, 0x50, 0x44, 0x46], PdfSha256 = "x", Status = PrintJobStatus.Routed,
            PrinterId = 10, CorrelationId = Guid.NewGuid(), CreatedAtUtc = now, UpdatedAtUtc = now
        });
        if (abortadoPorDocumento)
            db.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = jobId, EventType = "STATUS_CHANGE", OldStatus = PrintJobStatus.SpoolAccepted,
                NewStatus = PrintJobStatus.ErrorFinal, ErrorCode = "IPP_DOCUMENT_ERROR", OccurredAtUtc = now
            });
        await db.SaveChangesAsync();

        var cola = new FakeSpooler((_, _, _) => Task.FromResult(new PrintSpoolResult(true, null, null, false)));
        var ipp = new IppFalso(porIp, parada is null ? null : parada.Cancel);
        var service = new PrintExecutionService(
            db, simulacion ? new NoOpPrintSpooler(simulateSuccess: true) : soloIp ? new SinColaPrintSpooler() : cola, NullLogger<PrintExecutionService>.Instance,
            Options.Create(new PrintExecutionOptions { MaxAttempts = 4, BackoffSeconds = [15, 30, 60], TimeoutSeconds = 30 }),
            new RoutingSinUso(), TimeProvider.System, ipp);

        await service.ExecuteBatchAsync(10, parada?.Token ?? default);
        db.ChangeTracker.Clear();

        var job = await db.PrintJobs.SingleAsync(j => j.JobId == jobId);
        var mensaje = await db.PrintJobEvents.Where(e => e.JobId == jobId && e.NewStatus == job.Status)
            .Select(e => e.Message).FirstOrDefaultAsync();
        return (job.Status, ipp, cola, mensaje ?? "", job.IppJobId, job.LastErrorCode);
    }

    // alImprimir: lo que pasa mientras la impresora recibe el trabajo (por ejemplo, que se pare el servicio).
    private sealed class IppFalso(PrintSpoolResult? porIp, Action? alImprimir = null) : IIppConfirmationService
    {
        public int Llamadas { get; private set; }
        public bool ComoImagen { get; private set; }

        public Task<PrintSpoolResult?> PrintPdfAsync(string printerHost, byte[] pdf, string jobName, bool comoImagen, CancellationToken ct)
        {
            Llamadas++;
            ComoImagen = comoImagen;
            alImprimir?.Invoke();
            return Task.FromResult(porIp);
        }

        public Task<IppQueryResult> QueryPrinterStateAsync(string printerHost, CancellationToken ct)
            => Task.FromResult(new IppQueryResult(IppOutcome.Unavailable));

        public Task<IppQueryResult?> QueryJobStateAsync(string printerHost, int jobId, CancellationToken ct)
            => Task.FromResult<IppQueryResult?>(null);

        public Task<CancelacionEnImpresora> CancelJobAsync(string printerHost, int jobId, string jobName, CancellationToken ct)
            => Task.FromResult(CancelacionEnImpresora.SinRespuesta);
    }

    private sealed class RoutingSinUso : IRoutingResolver
    {
        public Task<int?> ResolvePrinterAsync(int storeId, string documentType, string channel, CancellationToken cancellationToken = default)
            => Task.FromResult<int?>(null);

        public Task<IReadOnlyList<int?>> ResolveBatchAsync(
            IReadOnlyList<(int storeId, string documentType, string channel)> requests, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<int?>>(requests.Select(_ => (int?)null).ToArray());

        public Task<RoutingRule?> ResolveRuleAsync(int storeId, string documentType, string channel, CancellationToken cancellationToken = default)
            => Task.FromResult<RoutingRule?>(null);
    }
}
