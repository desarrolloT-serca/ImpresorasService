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

    // R3: la cola de la propia impresora. Get-Jobs devuelve un grupo job-attributes (0x02) por trabajo.
    [Fact]
    public void La_cola_de_la_impresora_se_lee_por_trabajo_sin_motivos_de_fase()
    {
        var ms = new MemoryStream();
        ms.Write([0x01, 0x01, 0x00, 0x00, 0, 0, 0, 1, 0x01]); // operation-attributes
        void A(byte tag, string n, byte[] v)
        {
            var nb = Encoding.UTF8.GetBytes(n);
            ms.WriteByte(tag); ms.Write([(byte)(nb.Length >> 8), (byte)nb.Length]); ms.Write(nb);
            ms.Write([(byte)(v.Length >> 8), (byte)v.Length]); ms.Write(v);
        }
        A(0x47, "attributes-charset", Texto("utf-8"));
        ms.WriteByte(0x02);
        A(0x21, "job-id", Entero(160));
        A(0x36, "job-name", [0, 2, (byte)'e', (byte)'n', 0, 15, .. Encoding.UTF8.GetBytes("PRUEBA PRUEBA-1")]);
        A(0x23, "job-state", Entero(6));
        A(0x44, "job-state-reasons", Texto("job-incoming"));
        A(0x44, "", Texto("job-stopped"));
        A(0x36, "job-originating-user-name", [0, 2, (byte)'e', (byte)'n', 0, 17, .. Encoding.UTF8.GetBytes("ImpresorasService")]);
        A(0x21, "time-at-creation", Entero(745000));
        ms.WriteByte(0x02);
        A(0x21, "job-id", Entero(161));
        A(0x23, "job-state", Entero(3));
        ms.WriteByte(0x03);

        var trabajos = IppConfirmationService.LeerTrabajos(IppConfirmationService.ParseResponse(200, ms.ToArray()));

        Assert.Equal(2, trabajos.Count);
        Assert.Equal(new IppTrabajoImpresora(160, "PRUEBA PRUEBA-1", 6, "job-stopped", "ImpresorasService", 745000), trabajos[0]);
        Assert.Equal(new IppTrabajoImpresora(161, null, 3, null, null, null), trabajos[1]);
    }

    [Fact]
    public void Get_Jobs_es_la_operacion_0x000A_sin_which_jobs()
    {
        var peticion = IppConfirmationService.BuildGetJobsRequest("10.0.0.9", "/ipp/print");

        Assert.Equal(new byte[] { 0x01, 0x01, 0x00, 0x0A }, peticion[..4]);
        var texto = Encoding.UTF8.GetString(peticion);
        Assert.Contains("ImpresorasService", texto);
        Assert.Contains("job-originating-user-name", texto);
        Assert.DoesNotContain("which-jobs", texto); // la Brother L2710DW rechaza «not-completed»
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
