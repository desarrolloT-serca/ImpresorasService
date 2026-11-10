using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// Contador de páginas acumulado (ERP_SERCA docs/73, opción E). Medido el 10/11/2026 (docs/72 R0):
/// - Canon: no lo da por IPP ni responde a SNMP con «public»; su web sí, el contador 113 «Total (B/N,
///   pequeño)», que suma impresión, copias e informes.
/// - Brother y el resto: SNMP v2c, prtMarkerLifeCount de la Printer MIB (RFC 3805). La web de la Brother
///   pide contraseña en algunos modelos (la MFC-L2800DW).
/// Se elige por el modelo que ya leyó IPP; sin modelo, SNMP, que es el estándar.
/// </summary>
public sealed partial class LectorContador : ILectorContador
{
    public const string OrigenCanon = "canon-113";
    public const string OrigenSnmp = "snmp";

    // prtMarkerLifeCount.1.1: páginas desde fábrica del primer marcador.
    private static readonly int[] OidContador = [1, 3, 6, 1, 2, 1, 43, 10, 2, 1, 4, 1, 1];
    private static readonly TimeSpan Plazo = TimeSpan.FromSeconds(15);

    private readonly ILogger<LectorContador> _logger;

    public LectorContador(ILogger<LectorContador> logger) => _logger = logger;

    public async Task<LecturaContador?> LeerAsync(Printer impresora, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(impresora.Host)) return null;
        using var plazo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        plazo.CancelAfter(Plazo);
        try
        {
            if (impresora.IppMakeAndModel?.StartsWith("Canon", StringComparison.OrdinalIgnoreCase) == true)
                return await LeerCanonAsync(impresora.Host, plazo.Token);
            return await LeerSnmpAsync(impresora.Host, plazo.Token) is { } snmp ? new(snmp, OrigenSnmp) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or OperationCanceledException
                                       or UriFormatException or ArgumentException
                                   && !ct.IsCancellationRequested)
        {
            _logger.LogDebug("Sin contador de páginas de {Host}: {Error}", impresora.Host, ex.Message);
            return null;
        }
    }

    private static async Task<LecturaContador?> LeerCanonAsync(string host, CancellationToken ct)
    {
        // La web pide sesión: la cookie la da el monitor de estado. Sin pasar por él, el contador responde
        // «No se puede abrir esta página». Cada lectura, con su sesión.
        // ponytail: solo HTTP en el 8000, el de la IU remota de las Canon del parque; HTTPS (8443) si alguna
        // lo desactiva.
        using var http = new HttpClient(new SocketsHttpHandler { CookieContainer = new CookieContainer() })
            { Timeout = Timeout.InfiniteTimeSpan };
        var raiz = new Uri($"http://{host}:8000/");
        using (var monitor = await http.GetAsync(new Uri(raiz, "sysmonitor"), ct))
            monitor.EnsureSuccessStatusCode();
        if (ContadorCanon(await http.GetStringAsync(new Uri(raiz, "rps/dcounter.cgi?CorePGTAG=14"), ct)) is not { } total)
            return null;
        // Los días de tóner, con la misma sesión. Si esa página falla, el contador vale igual.
        int? dias = null;
        try
        {
            dias = DiasTonerCanon(await http.GetStringAsync(
                new Uri(raiz, "dvccom/consumables/?portalLang=es&disp=dstatus_menu"), ct));
        }
        catch (HttpRequestException)
        {
        }
        return new(total, OrigenCanon, dias);
    }

    /// <summary>
    /// Días que la Canon estima que le quedan al tóner (página «Consumibles»): en <c>tonerList</c>,
    /// <c>days:"15", daysKind:"WITH_IN_YEAR"</c>; con más de un año, <c>daysKind:"ONE_YEAR_OVER"</c>. Con
    /// varios tóneres, el que antes se acaba (ERP_SERCA docs/72).
    /// </summary>
    public static int? DiasTonerCanon(string html)
    {
        var dias = DiasToner().Matches(html)
            .Select(m => m.Groups[2].Value switch
            {
                "WITH_IN_YEAR" when int.TryParse(m.Groups[1].Value, out var d) => d,
                "ONE_YEAR_OVER" => LecturaContador.MasDeUnAno,
                _ => (int?)null
            })
            .Where(d => d is not null)
            .ToList();
        return dias.Count > 0 ? dias.Min() : null;
    }

    [GeneratedRegex("""\{item:"TONER_[^"]*"[^}]*?days:"(\d*)",\s*daysKind:"([A-Z_]*)"\}""")]
    private static partial Regex DiasToner();

    /// <summary>El 113 de la página «Revisar contadores»: <c>write_value("113",11090);</c>.</summary>
    public static long? ContadorCanon(string html) =>
        Contador113().Match(html) is { Success: true } m ? long.Parse(m.Groups[1].Value) : null;

    [GeneratedRegex("""write_value\("113",\s*(\d+)\)""")]
    private static partial Regex Contador113();

    private static async Task<long?> LeerSnmpAsync(string host, CancellationToken ct)
    {
        using var udp = new UdpClient();
        udp.Connect(host, 161);
        var id = Random.Shared.Next(1, int.MaxValue);
        await udp.SendAsync(Snmp.Get("public", id, OidContador), ct);
        var respuesta = await udp.ReceiveAsync(ct);
        return Snmp.Valor(respuesta.Buffer, id);
    }
}

/// <summary>
/// Lo justo de SNMP v2c para un GET de un OID (BER, RFC 3416): unas líneas en vez de una librería.
/// </summary>
public static class Snmp
{
    public static byte[] Get(string comunidad, int id, int[] oid) =>
        Tlv(0x30, [
            .. Entero(1), // versión: v2c
            .. Tlv(0x04, Encoding.ASCII.GetBytes(comunidad)),
            .. Tlv(0xA0, [ // GetRequest
                .. Entero(id), .. Entero(0), .. Entero(0),
                .. Tlv(0x30, Tlv(0x30, [.. Tlv(0x06, Oid(oid)), 0x05, 0x00])),
            ]),
        ]);

    /// <summary>
    /// El valor numérico de la respuesta (INTEGER, Counter32, Gauge32 o Counter64); null si no es la
    /// respuesta a <paramref name="id"/>, trae error, no tiene el objeto o está mal formada.
    /// </summary>
    public static long? Valor(byte[] respuesta, int id)
    {
        try
        {
            var i = 0;
            var (tag, mensaje) = Leer(respuesta, ref i);
            if (tag != 0x30) return null;
            var partes = Hijos(mensaje); // versión, comunidad, PDU
            if (partes.Count < 3 || partes[2].Tag != 0xA2) return null; // GetResponse
            var pdu = Hijos(partes[2].Valor); // id, error-status, error-index, varbinds
            if (pdu.Count < 4 || Numero(pdu[0].Valor) != id || Numero(pdu[1].Valor) != 0) return null;
            var varbind = Hijos(Hijos(pdu[3].Valor)[0].Valor); // OID, valor
            return varbind[1].Tag is 0x02 or 0x41 or 0x42 or 0x46 ? Numero(varbind[1].Valor) : null;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static byte[] Tlv(byte tag, byte[] contenido)
    {
        var n = contenido.Length;
        byte[] largo = n < 0x80 ? [(byte)n] : n < 0x100 ? [0x81, (byte)n] : [0x82, (byte)(n >> 8), (byte)n];
        return [tag, .. largo, .. contenido];
    }

    // Solo enteros no negativos: los únicos que se mandan.
    private static byte[] Entero(long v)
    {
        var bytes = new List<byte>();
        do
        {
            bytes.Insert(0, (byte)(v & 0xFF));
            v >>= 8;
        } while (v > 0);
        if ((bytes[0] & 0x80) != 0) bytes.Insert(0, 0);
        return Tlv(0x02, [.. bytes]);
    }

    private static byte[] Oid(int[] arcos)
    {
        var r = new List<byte> { (byte)(40 * arcos[0] + arcos[1]) };
        foreach (var arco in arcos.Skip(2))
        {
            var trozo = new List<byte> { (byte)(arco & 0x7F) };
            for (var v = arco >> 7; v > 0; v >>= 7) trozo.Insert(0, (byte)(0x80 | (v & 0x7F)));
            r.AddRange(trozo);
        }
        return [.. r];
    }

    private static (byte Tag, byte[] Valor) Leer(byte[] b, ref int i)
    {
        var tag = b[i++];
        int n = b[i++];
        if ((n & 0x80) != 0)
        {
            var k = n & 0x7F;
            n = 0;
            for (var j = 0; j < k; j++) n = (n << 8) | b[i++];
        }
        var valor = b[i..(i + n)];
        i += n;
        return (tag, valor);
    }

    private static List<(byte Tag, byte[] Valor)> Hijos(byte[] b)
    {
        var r = new List<(byte, byte[])>();
        var i = 0;
        while (i < b.Length) r.Add(Leer(b, ref i));
        return r;
    }

    private static long Numero(byte[] v) => v.Aggregate(0L, (r, x) => (r << 8) | x);
}
