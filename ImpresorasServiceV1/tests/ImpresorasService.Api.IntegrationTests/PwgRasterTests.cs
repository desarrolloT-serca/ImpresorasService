using System.Buffers.Binary;
using System.Text;
using ImpresorasService.Infrastructure.Services;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// PDF → PWG raster para imprimir por IP en impresoras sin PDF (B9.3). El decodificador de estas
/// pruebas está escrito aparte, a partir de PWG 5102.4, para no validar el formato consigo mismo.
/// </summary>
public sealed class PwgRasterTests
{
    [Fact]
    public void Dibuja_el_PDF_sobre_blanco_y_lo_codifica_en_PWG()
    {
        // A4 con un rectángulo negro de 200×50 pt a 100 pt de la izquierda y de abajo.
        var pdf = Pdf("0 0 0 rg 100 100 200 50 re f");

        var pwg = PwgRaster.DesdePdf(pdf, dpi: 72, color: false);

        Assert.Equal("RaS2", Encoding.ASCII.GetString(pwg, 0, 4));
        var (cabecera, pixeles, resto) = LeerPagina(pwg, 4);
        Assert.Equal(0, resto);
        var ancho = U32(cabecera, 372);
        var alto = U32(cabecera, 376);
        Assert.Equal((595, 842), (ancho, alto));
        Assert.Equal(72, U32(cabecera, 276));       // HWResolution
        Assert.Equal(18, U32(cabecera, 400));       // sGray
        Assert.Equal(1, U32(cabecera, 452));        // TotalPageCount
        // Con NumCopies a 0 la Brother repitió la hoja sin fin (26/10/2026).
        Assert.Equal(1, U32(cabecera, 340));        // NumCopies
        Assert.Equal((595, 842), (U32(cabecera, 472), U32(cabecera, 476))); // ImageBoxRight, ImageBoxBottom
        Assert.Equal("iso_a4_210x297mm", Encoding.ASCII.GetString(cabecera, 1732, 16));
        Assert.Equal(255, pixeles[10 * ancho + 10]);             // papel
        Assert.True(pixeles[(842 - 125) * ancho + 200] < 30);   // dentro del rectángulo
    }

    [Fact]
    public void Las_filas_comprimidas_se_recuperan_tal_cual()
    {
        const int ancho = 300, alto = 6;
        var pixeles = new byte[ancho * alto];
        Array.Fill(pixeles, (byte)255);                                  // filas 0–2 blancas e iguales
        for (var x = 0; x < ancho; x++) pixeles[3 * ancho + x] = (byte)(x % 7 * 30); // literales
        pixeles[4 * ancho + 150] = 0;                                    // un punto suelto
        for (var x = 0; x < ancho; x++) pixeles[5 * ancho + x] = (byte)(x < 200 ? 0 : x); // corrida larga

        using var ms = new MemoryStream();
        PwgRaster.EscribirPagina(ms, pixeles, ancho, alto, dpi: 300, color: false, totalPaginas: 1);
        var (cabecera, leidos, resto) = LeerPagina(ms.ToArray(), 0);

        Assert.Equal(0, resto);
        Assert.Equal(ancho, U32(cabecera, 392)); // BytesPerLine
        Assert.Equal(pixeles, leidos);
    }

    private static int U32(byte[] b, int offset) => (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(offset));

    // Una página en escala de grises: cabecera de 1796 bytes y filas comprimidas. Devuelve lo que
    // sobra detrás, que en una página sola debe ser 0.
    private static (byte[] Cabecera, byte[] Pixeles, int Resto) LeerPagina(byte[] datos, int inicio)
    {
        var cabecera = datos[inicio..(inicio + 1796)];
        int ancho = U32(cabecera, 372), alto = U32(cabecera, 376);
        var pixeles = new byte[ancho * alto];
        var i = inicio + 1796;
        for (var y = 0; y < alto;)
        {
            var repeticion = datos[i++] + 1;
            var fila = new byte[ancho];
            for (var x = 0; x < ancho;)
            {
                int n = datos[i++];
                if (n <= 127)
                {
                    Array.Fill(fila, datos[i++], x, n + 1);
                    x += n + 1;
                }
                else
                {
                    var literales = 257 - n;
                    Array.Copy(datos, i, fila, x, literales);
                    i += literales;
                    x += literales;
                }
            }
            for (var r = 0; r < repeticion; r++)
                fila.CopyTo(pixeles, (y + r) * ancho);
            y += repeticion;
        }
        return (cabecera, pixeles, datos.Length - i);
    }

    // PDF de una página A4 con el contenido dado, con su tabla de referencias bien calculada.
    private static byte[] Pdf(string contenido)
    {
        var objetos = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R >>",
            $"<< /Length {contenido.Length} >>\nstream\n{contenido}\nendstream"
        };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var o = 0; o < objetos.Length; o++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{o + 1} 0 obj\n{objetos[o]}\nendobj\n");
        }
        var xref = sb.Length;
        sb.Append($"xref\n0 {objetos.Length + 1}\n0000000000 65535 f \n");
        foreach (var off in offsets) sb.Append($"{off:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objetos.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
