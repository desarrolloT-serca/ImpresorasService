using System.Text;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Services;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// ERP_SERCA docs/72, paquete del 10/11/2026: «desde cuándo» del estado (3), Identify-Printer (4),
/// salud de la tienda con el estado de la impresora (5), ficha (6) y previsión del tóner (7).
/// </summary>
public sealed class EstadoImpresoraTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);

    // Lo que contestan la Canon imageFORCE 1643 y la Brother MFC-L2800DW (leído el 10/11/2026).
    [Fact]
    public void La_ficha_lee_firmware_tiempo_encendida_y_bandejas_sin_la_auto()
    {
        var r = IppConfirmationService.ParseResponse(200, Respuesta(
            (0x41, "printer-firmware-string-version", Texto("ZA2211160120:9FD6")),
            (0x41, "", Texto("1.05")),
            (0x21, "printer-up-time", Entero(97197)),
            (0x30, "printer-input-tray", Texto("type=other;mediafeed=29700;maxcapacity=550;level=1100;status=0;name=auto;")),
            (0x30, "", Texto("type=sheetFeedAutoRemovableTray;mediafeed=29700;maxcapacity=550;level=1100;status=0;name=Drawer 1;")),
            (0x30, "", Texto("type=sheetFeedManual;mediafeed=-2;maxcapacity=1;level=-3;status=0;name=MANUAL TRAY;index=2;"))));

        var e = IppConfirmationService.LeerEstado(r);

        Assert.Equal("ZA2211160120:9FD6, 1.05", e.Firmware);
        Assert.Equal(97197, e.EncendidaSegundos);
        Assert.Equal([new IppBandeja("Drawer 1", 550, 1100), new IppBandeja("MANUAL TRAY", 1, -3)], e.Bandejas);

        var p = new Printer();
        IppEstadoImpresora.AplicarA(p, e, Ahora);
        Assert.Equal(Ahora.AddSeconds(-97197), p.IppUpSinceUtc);
        Assert.Contains("Drawer 1", p.IppTraysJson);
    }

    [Fact]
    public void Desde_se_mantiene_mientras_no_cambian_estado_ni_motivos()
    {
        var antes = Ahora.AddHours(-5);
        var tambor = Estado(3, "opc-life-over-warning");

        Assert.Equal(antes, tambor.Desde(3, "opc-life-over-warning", antes, Ahora));
        Assert.Equal(Ahora, tambor.Desde(3, null, antes, Ahora));                    // apareció ahora
        Assert.Equal(Ahora, tambor.Desde(5, "opc-life-over-warning", antes, Ahora)); // cambió el estado
        Assert.Equal(Ahora, tambor.Desde(3, "opc-life-over-warning", null, Ahora));  // sin dato anterior

        var p = new Printer();
        IppEstadoImpresora.AplicarA(p, tambor, Ahora, antes);
        Assert.Equal(antes, p.IppStateSinceUtc);
        IppEstadoImpresora.AplicarA(p, null, Ahora);
        Assert.Null(p.IppStateSinceUtc);
    }

    [Fact]
    public void Identify_Printer_es_la_operacion_0x003C_con_el_usuario_del_servicio()
    {
        var peticion = IppConfirmationService.BuildIdentifyRequest("10.0.0.9", "/ipp/print");

        Assert.Equal(new byte[] { 0x01, 0x01, 0x00, 0x3C }, peticion[..4]);
        var texto = Encoding.UTF8.GetString(peticion);
        Assert.Contains("ipp://10.0.0.9:631/ipp/print", texto);
        Assert.Contains("ImpresorasService", texto);
    }

    [Fact]
    public void Solo_se_guarda_lectura_del_toner_cuando_cambia_su_nivel()
    {
        var antes = """[{"nombre":"BK","color":null,"tipo":"toner","nivel":83,"bajo":7}]""";
        Assert.Empty(TonerReadings.Nuevas(6, antes, Con(83), Ahora));

        var l = Assert.Single(TonerReadings.Nuevas(6, antes, Con(82), Ahora));
        Assert.Equal((6, 0, 82, Ahora), (l.PrinterId, l.Marker, l.Level, l.ReadAtUtc));

        Assert.Single(TonerReadings.Nuevas(6, null, Con(82), Ahora));     // primera lectura
        Assert.Empty(TonerReadings.Nuevas(6, null, Con(-3), Ahora));      // «queda algo»: no es un %
    }

    [Fact]
    public void Previsión_del_toner_desde_el_ultimo_cartucho()
    {
        // 1 % al día: de 30 % a 10 % (crítico) quedan 20 días.
        var lecturas = Enumerable.Range(0, 11).Select(d => (Ahora.AddDays(d - 10), 40 - d)).ToList();
        Assert.Equal(20, TonerReadings.DiasHasta(lecturas, 10, Ahora));

        // Cartucho nuevo: lo de antes del cambio no cuenta.
        var conCambio = new List<(DateTimeOffset, int)> { (Ahora.AddDays(-30), 5), (Ahora.AddDays(-20), 3) };
        conCambio.AddRange(Enumerable.Range(0, 11).Select(d => (Ahora.AddDays(d - 10), 100 - 2 * d)));
        Assert.Equal(35, TonerReadings.DiasHasta(conCambio, 10, Ahora));

        Assert.Equal(0, TonerReadings.DiasHasta([(Ahora, 9)], 10, Ahora));                                  // ya está
        Assert.Null(TonerReadings.DiasHasta([(Ahora.AddHours(-2), 50), (Ahora, 49)], 10, Ahora));            // menos de un día
        Assert.Null(TonerReadings.DiasHasta([(Ahora.AddDays(-5), 50), (Ahora, 50)], 10, Ahora));             // no baja
        Assert.Null(TonerReadings.DiasHasta([], 10, Ahora));
    }

    [Fact]
    public void La_impresora_parada_o_con_toner_critico_pone_la_tienda_en_critica_y_avisa()
    {
        var rules = ThresholdRuleSet.Default;
        var (health, reason) = StoreHealthEvaluator.Compute(2, 0, 0, 0, 0, 0, 0, rules, ippCritical: 1);
        Assert.Equal(("critical", StoreHealthEvaluator.IppCriticalReason), (health, reason));

        Assert.Equal("warning", StoreHealthEvaluator.Compute(2, 0, 0, 0, 0, 0, 0, rules, ippWarning: 2).Health);
        // La conexión manda: sin conexión no hay lectura que valga.
        Assert.Equal("Impresora(s) sin conexion (conectividad)",
            StoreHealthEvaluator.Compute(2, 0, 0, 0, 0, 1, 0, rules, ippCritical: 1).Reason);

        var alertas = StoreHealthEvaluator.BuildAlerts(
            [new StoreAlertInput(6, "T6", 2, 0, 0, 0, 0, 0, 0, IppCritical: 1, IppWarning: 1)], rules);
        var a = Assert.Single(alertas);
        Assert.Equal(("critical", StoreHealthEvaluator.IppCriticalReason), (a.Health, a.HealthReason));
    }

    private static IppEstadoImpresora Estado(int estado, string? motivos) =>
        new(estado, motivos, null, 0, [], null, null);

    private static IppEstadoImpresora Con(int nivel) =>
        new(3, null, null, 0, [new IppConsumible("BK", null, "toner", nivel, 7)], null, null);

    private static byte[] Texto(string s) => Encoding.UTF8.GetBytes(s);
    private static byte[] Entero(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static byte[] Respuesta(params (byte Tag, string Nombre, byte[] Valor)[] atributos)
    {
        var ms = new MemoryStream();
        ms.Write([0x01, 0x01, 0x00, 0x00, 0, 0, 0, 1, 0x04]); // printer-attributes
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
}
