using System.Net;
using ImpresorasService.Api.IntegrationTests.Flow;
using ImpresorasService.Application.Models;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain;
using ImpresorasService.Infrastructure;
using ImpresorasService.Infrastructure.Adapters;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Repositories;
using ImpresorasService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// El ERP deja la URL del PDF en pdf_url y la ingesta lo descarga (decisión del usuario, 26/10/2026).
/// Un fallo pasajero se reintenta; uno definitivo deja el trabajo a la vista en ErrorFinal.
/// </summary>
public sealed class PdfUrlTests
{
    private static readonly string[] Hosts = ["facturas.repuestos-serca.es"];
    private const string Url = "https://facturas.repuestos-serca.es/generate.php?id=SECRETO&inline=0";

    [Fact]
    public async Task Descarga_el_PDF_del_host_permitido()
    {
        var (pdf, error, retry) = await DescargarAsync(HttpStatusCode.OK, MinimalPdf.Bytes);

        Assert.Equal(MinimalPdf.Bytes, pdf);
        Assert.Null(error);
        Assert.False(retry);
    }

    [Theory]
    [InlineData("http://facturas.repuestos-serca.es/generate.php?id=SECRETO")]
    [InlineData("https://otro.example.com/generate.php?id=SECRETO")]
    [InlineData("no es una url")]
    public async Task Sin_https_o_fuera_de_los_hosts_no_se_descarga(string url)
    {
        var handler = new Respuesta(HttpStatusCode.OK, MinimalPdf.Bytes);
        var (pdf, error, retry) = await ErpJobSourceAdapter.DownloadPdfAsync(new HttpClient(handler), url, Hosts, default);

        Assert.Null(pdf);
        Assert.False(retry);
        Assert.Equal(0, handler.Llamadas);
        Assert.DoesNotContain("SECRETO", error);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Found, false)] // sin redirecciones: podrían salir de los hosts permitidos
    public async Task Los_5xx_se_reintentan_y_los_4xx_no(HttpStatusCode status, bool reintentar)
    {
        var (pdf, error, retry) = await DescargarAsync(status, []);

        Assert.Null(pdf);
        Assert.Equal(reintentar, retry);
        Assert.DoesNotContain("SECRETO", error);
    }

    [Fact]
    public async Task Lo_que_no_es_un_PDF_es_error_definitivo()
    {
        var (pdf, error, retry) = await DescargarAsync(HttpStatusCode.OK, "<html>login</html>"u8.ToArray());

        Assert.Null(pdf);
        Assert.False(retry);
        Assert.Contains("no devolvió un PDF", error);
    }

    [Fact]
    public async Task Sin_respuesta_se_reintenta()
    {
        var http = new HttpClient(new Respuesta(new HttpRequestException("No such host is known.")));
        var (pdf, _, retry) = await ErpJobSourceAdapter.DownloadPdfAsync(http, Url, Hosts, default);

        Assert.Null(pdf);
        Assert.True(retry);
    }

    [Fact]
    public async Task Si_la_URL_no_sirve_el_trabajo_entra_en_ErrorFinal_sin_enrutar_y_se_acusa()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;
        var now = DateTimeOffset.UtcNow;
        db.SourcePrintJobs.Add(new SourcePrintJobRecord
        {
            Id = 1, SourceSystem = "ERP", ExternalJobId = "EXT-URL", StoreId = 1, DocumentType = "FACTURA",
            PdfBlob = null, PdfUrl = Url, CreatedAtUtc = now
        });
        await db.SaveChangesAsync();

        var adapter = new StaticJobSourceAdapter(db,
        [
            new IncomingPrintJob(1, "ERP", "EXT-URL", 1, "FACTURA", "DEFAULT", [], now,
                PdfError: "facturas.repuestos-serca.es respondió 404.")
        ]);
        var ingestion = new IngestionService(adapter, new PrintJobRepository(db),
            new RoutingService(db, new RoutingResolver(db)), NullLogger<IngestionService>.Instance, TimeProvider.System);

        await ingestion.IngestBatchAsync(10, default);

        var job = await db.PrintJobs.SingleAsync();
        Assert.Equal(PrintJobStatus.ErrorFinal, job.Status);
        Assert.Equal(IngestionService.PdfDownloadFailedCode, job.LastErrorCode);
        Assert.Null(job.PrinterId);
        Assert.Null(job.PdfBlob);
        Assert.True((await db.SourcePrintJobs.SingleAsync()).IsProcessed);
        Assert.Single(await db.PrintJobEvents.Where(e => e.JobId == job.JobId).ToListAsync());
    }

    // Un pdf_url que falla por la red se reintenta un rato; pasado el plazo, entra con el error y queda a
    // la vista en la cola (auditoría L16). localhost:1 rechaza la conexión sin salir del equipo.
    [Theory]
    [InlineData(0, false)]
    [InlineData(15, true)]
    public async Task Un_PDF_que_no_se_puede_descargar_se_reintenta_solo_un_rato(int minutosDesdeQueLlego, bool entra)
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;
        db.SourcePrintJobs.Add(new SourcePrintJobRecord
        {
            Id = 1, SourceSystem = "ERP", ExternalJobId = "EXT-URL", StoreId = 1, DocumentType = "FACTURA",
            PdfUrl = "https://localhost:1/albaran.pdf", CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-minutosDesdeQueLlego)
        });
        await db.SaveChangesAsync();
        var adapter = new ErpJobSourceAdapter(
            Microsoft.Extensions.Options.Options.Create(new Infrastructure.Options.SourceOptions { PdfUrlHosts = ["localhost"] }),
            db, NullLogger<ErpJobSourceAdapter>.Instance, TimeProvider.System);

        var jobs = await adapter.FetchPendingJobsAsync(10, default);

        Assert.Equal(entra, jobs.Count == 1);
        if (entra)
            Assert.Contains("Se dejó de reintentar", jobs[0].PdfError);
    }

    private static Task<(byte[]? Pdf, string? Error, bool Retry)> DescargarAsync(HttpStatusCode status, byte[] body) =>
        ErpJobSourceAdapter.DownloadPdfAsync(new HttpClient(new Respuesta(status, body)), Url, Hosts, default);

    [Fact]
    public async Task Un_cuerpo_que_se_queda_parado_se_corta_y_se_reintenta()
    {
        var http = new HttpClient(new CuerpoParado()) { Timeout = TimeSpan.FromMilliseconds(200) };

        var (pdf, _, retry) = await ErpJobSourceAdapter.DownloadPdfAsync(http, Url, Hosts, default)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(pdf);
        Assert.True(retry);
    }

    // Manda las cabeceras y después no manda nada del cuerpo.
    private sealed class CuerpoParado : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Parado()) });

        private sealed class Parado : Stream
        {
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
            {
                await Task.Delay(Timeout.Infinite, ct);
                return 0;
            }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    private sealed class Respuesta : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly byte[] _body = [];
        private readonly Exception? _fallo;
        public int Llamadas { get; private set; }

        public Respuesta(HttpStatusCode status, byte[] body) => (_status, _body) = (status, body);
        public Respuesta(Exception fallo) => _fallo = fallo;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Llamadas++;
            if (_fallo is not null)
                throw _fallo;
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new ByteArrayContent(_body) });
        }
    }
}
