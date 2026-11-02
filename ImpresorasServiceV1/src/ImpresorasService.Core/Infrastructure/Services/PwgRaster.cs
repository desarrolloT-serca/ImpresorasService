using System.Buffers.Binary;
using System.Text;
using Docnet.Core;
using Docnet.Core.Models;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// PDF → PWG raster (PWG 5102.4, el formato de imagen de IPP Everywhere), para imprimir por IP en
/// impresoras que no aceptan PDF (por ejemplo, la Brother MFC-L2800DW). Las páginas las dibuja PDFium
/// (Docnet.Core) sobre blanco; cada página lleva su cabecera de 1796 bytes y las líneas comprimidas.
/// </summary>
internal static class PwgRaster
{
    private const int TamCabecera = 1796;

    public static byte[] DesdePdf(byte[] pdf, int dpi, bool color)
    {
        using var salida = new MemoryStream();
        salida.Write("RaS2"u8);
        // DocLib es un singleton de Docnet: no se libera.
        using var doc = DocLib.Instance.GetDocReader(pdf, new PageDimensions(dpi / 72.0));
        var paginas = doc.GetPageCount();
        for (var i = 0; i < paginas; i++)
        {
            using var pagina = doc.GetPageReader(i);
            var ancho = pagina.GetPageWidth();
            var alto = pagina.GetPageHeight();
            EscribirPagina(salida, SobreBlanco(pagina.GetImage(), color), ancho, alto, dpi, color, paginas);
        }
        return salida.ToArray();
    }

    // PDFium devuelve BGRA con transparencia donde no hay nada: se compone sobre papel blanco.
    internal static byte[] SobreBlanco(byte[] bgra, bool color)
    {
        var pixeles = bgra.Length / 4;
        var salida = new byte[pixeles * (color ? 3 : 1)];
        for (var p = 0; p < pixeles; p++)
        {
            int b = bgra[p * 4], g = bgra[p * 4 + 1], r = bgra[p * 4 + 2], a = bgra[p * 4 + 3];
            byte Componer(int c) => (byte)((c * a + 255 * (255 - a)) / 255);
            if (color)
            {
                salida[p * 3] = Componer(r);
                salida[p * 3 + 1] = Componer(g);
                salida[p * 3 + 2] = Componer(b);
            }
            else
                salida[p] = Componer((r * 299 + g * 587 + b * 114) / 1000);
        }
        return salida;
    }

    // Cabecera (offsets de cups_page_header2_t, que es la que define PWG 5102.4) y filas.
    internal static void EscribirPagina(Stream s, byte[] pixeles, int ancho, int alto, int dpi, bool color, int totalPaginas)
    {
        var bytesPorPixel = color ? 3 : 1;
        var h = new byte[TamCabecera];
        void U32(int offset, uint valor) => BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(offset), valor);

        // Mismos valores por defecto que cupsRasterInitPWGHeader. NumCopies a 0 hizo que la Brother
        // MFC-L2800DW repitiera la hoja sin fin (18 hasta cancelarla, 26/10/2026): siempre 1.
        Encoding.ASCII.GetBytes("PwgRaster").CopyTo(h, 0);           // MediaClass
        var puntosAncho = (uint)Math.Round(ancho * 72.0 / dpi);
        var puntosAlto = (uint)Math.Round(alto * 72.0 / dpi);
        U32(276, (uint)dpi); U32(280, (uint)dpi);                    // HWResolution
        U32(292, puntosAncho); U32(296, puntosAlto);                  // ImagingBoundingBox (derecha, arriba)
        U32(340, 1);                                                  // NumCopies
        U32(352, puntosAncho); U32(356, puntosAlto);                  // PageSize, en puntos
        U32(372, (uint)ancho); U32(376, (uint)alto);                  // Width, Height
        U32(384, 8);                                                  // BitsPerColor
        U32(388, (uint)(8 * bytesPorPixel));                          // BitsPerPixel
        U32(392, (uint)(ancho * bytesPorPixel));                      // BytesPerLine
        U32(396, 0);                                                  // ColorOrder: chunky
        U32(400, color ? 19u : 18u);                                  // ColorSpace: sRGB o sGray
        U32(420, (uint)bytesPorPixel);                                // NumColors
        U32(452, (uint)totalPaginas);                                 // TotalPageCount
        U32(456, 1); U32(460, 1);                                     // CrossFeedTransform, FeedTransform
        U32(472, (uint)ancho); U32(476, (uint)alto);                  // ImageBoxRight, ImageBoxBottom
        U32(480, 0x00FFFFFF);                                         // AlternatePrimary
        if (Math.Abs(ancho * 72.0 / dpi - 595) < 3 && Math.Abs(alto * 72.0 / dpi - 842) < 3)
            Encoding.ASCII.GetBytes("iso_a4_210x297mm").CopyTo(h, 1732); // PageSizeName
        s.Write(h);

        var fila = ancho * bytesPorPixel;
        for (var y = 0; y < alto;)
        {
            // Filas iguales seguidas: un byte de repetición (0 = una vez) y la fila una sola vez.
            var repetidas = 0;
            while (repetidas < 255 && y + repetidas + 1 < alto
                && pixeles.AsSpan((y + repetidas + 1) * fila, fila).SequenceEqual(pixeles.AsSpan(y * fila, fila)))
                repetidas++;
            s.WriteByte((byte)repetidas);
            EscribirFila(s, pixeles.AsSpan(y * fila, fila), bytesPorPixel);
            y += repetidas + 1;
        }
    }

    // 0–127: el píxel siguiente se repite n+1 veces. 129–255: siguen 257-n píxeles tal cual (2–128).
    // 128 no se usa.
    private static void EscribirFila(Stream s, ReadOnlySpan<byte> fila, int bpp)
    {
        var n = fila.Length / bpp;
        static bool Igual(ReadOnlySpan<byte> f, int bpp, int a, int b) =>
            f.Slice(a * bpp, bpp).SequenceEqual(f.Slice(b * bpp, bpp));
        var i = 0;
        while (i < n)
        {
            var corrida = 1;
            while (corrida < 128 && i + corrida < n && Igual(fila, bpp, i, i + corrida))
                corrida++;
            if (corrida > 1 || i + 1 == n)
            {
                s.WriteByte((byte)(corrida - 1));
                s.Write(fila.Slice(i * bpp, bpp));
                i += corrida;
                continue;
            }

            var literales = 1;
            while (literales < 128 && i + literales < n
                && !(i + literales + 1 < n && Igual(fila, bpp, i + literales, i + literales + 1)))
                literales++;
            if (literales == 1)
            {
                s.WriteByte(0);
                s.Write(fila.Slice(i * bpp, bpp));
            }
            else
            {
                s.WriteByte((byte)(257 - literales));
                s.Write(fila.Slice(i * bpp, literales * bpp));
            }
            i += literales;
        }
    }
}
