using System.Buffers.Binary;
using System.Text;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task Con_IP_que_acepta_PDF_sale_por_IP_y_no_toca_la_cola()
    {
        var (estado, ipp, cola, mensaje, ippJobId) = await ImprimirAsync(host: "10.0.0.9", queue: "COLA", porIp: Aceptado);

        Assert.Equal(PrintJobStatus.SpoolAccepted, estado);
        Assert.Equal(1, ipp.Llamadas);
        Assert.Equal(0, cola.CallCount);
        Assert.Contains("por IP", mensaje);
        Assert.Equal(7, ippJobId); // con él, el watchdog confirma ese trabajo
    }

    [Fact]
    public async Task Si_por_IP_no_se_puede_va_por_la_cola()
    {
        var (estado, ipp, cola, _, ippJobId) = await ImprimirAsync(host: "10.0.0.9", queue: "COLA", porIp: null);

        Assert.Equal(PrintJobStatus.SpoolAccepted, estado);
        Assert.Equal(1, ipp.Llamadas);
        Assert.Equal("COLA", cola.LastQueueName);
        Assert.Null(ippJobId);
    }

    [Fact]
    public void Get_Job_Attributes_pide_el_trabajo_por_su_numero()
    {
        var peticion = IppConfirmationService.BuildGetJobAttributesRequest("10.0.0.9", "/ipp/print", 258);

        Assert.Equal(0x0009, BinaryPrimitives.ReadUInt16BigEndian(peticion.AsSpan(2)));
        var nombre = Encoding.UTF8.GetBytes("job-id");
        var i = peticion.AsSpan().IndexOf(nombre);
        Assert.Equal(0x21, peticion[i - 3]); // integer
        Assert.Equal(258, BinaryPrimitives.ReadInt32BigEndian(peticion.AsSpan(i + nombre.Length + 2)));
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
        if (jobState == 8) Assert.Contains("media-jam", estado.ErrorMessage);
    }

    [Fact]
    public async Task Un_envio_por_IP_cortado_no_se_repite_por_la_cola()
    {
        var cortado = new PrintSpoolResult(false, "NET_TIMEOUT", "El envío por IP se cortó sin conocerse el resultado.", false);

        var (estado, _, cola, _, _) = await ImprimirAsync(host: "10.0.0.9", queue: "COLA", porIp: cortado);

        Assert.Equal(PrintJobStatus.PrintedUnknown, estado);
        Assert.Equal(0, cola.CallCount);
    }

    [Fact]
    public async Task Sin_IP_va_directo_por_la_cola()
    {
        var (estado, ipp, cola, _, _) = await ImprimirAsync(host: null, queue: "COLA", porIp: Aceptado);

        Assert.Equal(PrintJobStatus.SpoolAccepted, estado);
        Assert.Equal(0, ipp.Llamadas);
        Assert.Equal(1, cola.CallCount);
    }

    [Fact]
    public async Task Solo_IP_y_la_impresora_no_puede_se_reintenta()
    {
        var (estado, _, cola, _, _) = await ImprimirAsync(host: "10.0.0.9", queue: null, porIp: null);

        Assert.Equal(PrintJobStatus.RetryScheduled, estado);
        Assert.Equal(0, cola.CallCount);
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

    private static async Task<(PrintJobStatus Estado, IppFalso Ipp, FakeSpooler Cola, string Mensaje, int? IppJobId)> ImprimirAsync(
        string? host, string? queue, PrintSpoolResult? porIp)
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;
        var now = DateTimeOffset.UtcNow;
        db.Stores.Add(new Store { StoreId = 1, Name = "T1", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.Printers.Add(new Printer
        {
            PrinterId = 10, PrinterName = "P", SpoolQueue = queue, Host = host, StoreId = 1,
            IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now
        });
        var jobId = Guid.NewGuid();
        db.PrintJobs.Add(new PrintJob
        {
            JobId = jobId, SourceSystem = "TEST", ExternalJobId = "A-1", StoreId = 1, DocumentType = "ALBARAN",
            Channel = "DEFAULT", PdfBlob = [0x25, 0x50, 0x44, 0x46], PdfSha256 = "x", Status = PrintJobStatus.Routed,
            PrinterId = 10, CorrelationId = Guid.NewGuid(), CreatedAtUtc = now, UpdatedAtUtc = now
        });
        await db.SaveChangesAsync();

        var cola = new FakeSpooler((_, _, _) => Task.FromResult(new PrintSpoolResult(true, null, null, false)));
        var ipp = new IppFalso(porIp);
        var service = new PrintExecutionService(
            db, cola, NullLogger<PrintExecutionService>.Instance,
            Options.Create(new PrintExecutionOptions { MaxAttempts = 4, BackoffSeconds = [15, 30, 60], TimeoutSeconds = 30 }),
            new RoutingSinUso(), TimeProvider.System, ipp);

        await service.ExecuteBatchAsync(10);
        db.ChangeTracker.Clear();

        var job = await db.PrintJobs.SingleAsync(j => j.JobId == jobId);
        var mensaje = await db.PrintJobEvents.Where(e => e.JobId == jobId && e.NewStatus == job.Status)
            .Select(e => e.Message).FirstOrDefaultAsync();
        return (job.Status, ipp, cola, mensaje ?? "", job.IppJobId);
    }

    private sealed class IppFalso(PrintSpoolResult? porIp) : IIppConfirmationService
    {
        public int Llamadas { get; private set; }

        public Task<PrintSpoolResult?> PrintPdfAsync(string printerHost, byte[] pdf, string jobName, CancellationToken ct)
        {
            Llamadas++;
            return Task.FromResult(porIp);
        }

        public Task<IppQueryResult> QueryPrinterStateAsync(string printerHost, CancellationToken ct)
            => Task.FromResult(new IppQueryResult(IppOutcome.Unavailable));

        public Task<IppQueryResult?> QueryJobStateAsync(string printerHost, int jobId, CancellationToken ct)
            => Task.FromResult<IppQueryResult?>(null);
    }

    private sealed class RoutingSinUso : IRoutingResolver
    {
        public Task<int?> ResolvePrinterAsync(int storeId, string documentType, string channel, CancellationToken cancellationToken = default)
            => Task.FromResult<int?>(null);

        public Task<IReadOnlyList<int?>> ResolveBatchAsync(
            IReadOnlyList<(int storeId, string documentType, string channel)> requests, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<int?>>(requests.Select(_ => (int?)null).ToArray());
    }
}
