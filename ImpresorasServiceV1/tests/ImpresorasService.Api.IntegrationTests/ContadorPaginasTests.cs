using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests;

/// <summary>
/// Contador de páginas diario (ERP_SERCA docs/73, opción E): cómo se lee de la impresora, qué se guarda
/// cada noche y cómo salen las páginas de un rango de días.
/// </summary>
public sealed class ContadorPaginasTests
{
    private static readonly DateOnly Dia = new(2026, 11, 10);
    private static readonly int[] Oid = [1, 3, 6, 1, 2, 1, 43, 10, 2, 1, 4, 1, 1];

    // GET de prtMarkerLifeCount.1.1 con «public» y id 1, byte a byte (RFC 3416, BER).
    [Fact]
    public void Snmp_Get_CodificaLaPeticion()
    {
        byte[] esperado =
        [
            0x30, 0x2A, 0x02, 0x01, 0x01, 0x04, 0x06, (byte)'p', (byte)'u', (byte)'b', (byte)'l', (byte)'i', (byte)'c',
            0xA0, 0x1D, 0x02, 0x01, 0x01, 0x02, 0x01, 0x00, 0x02, 0x01, 0x00,
            0x30, 0x12, 0x30, 0x10, 0x06, 0x0C, 0x2B, 0x06, 0x01, 0x02, 0x01, 0x2B, 0x0A, 0x02, 0x01, 0x04, 0x01, 0x01,
            0x05, 0x00
        ];
        Assert.Equal(esperado, Snmp.Get("public", 1, Oid));
    }

    [Fact]
    public void Snmp_Valor_LeeElContadorYDescartaLoQueNoEs()
    {
        // GetResponse id 7 con Counter32 = 61783 (0xF157, con un 0 delante por el bit de signo).
        byte[] Respuesta(int id, int error, byte tipo, byte[] valor) =>
        [
            0x30, (byte)(0x2A + valor.Length), 0x02, 0x01, 0x01, 0x04, 0x06, (byte)'p', (byte)'u', (byte)'b', (byte)'l', (byte)'i', (byte)'c',
            0xA2, (byte)(0x1D + valor.Length), 0x02, 0x01, (byte)id, 0x02, 0x01, (byte)error, 0x02, 0x01, 0x00,
            0x30, (byte)(0x12 + valor.Length), 0x30, (byte)(0x10 + valor.Length),
            0x06, 0x0C, 0x2B, 0x06, 0x01, 0x02, 0x01, 0x2B, 0x0A, 0x02, 0x01, 0x04, 0x01, 0x01,
            tipo, (byte)valor.Length, .. valor
        ];

        Assert.Equal(61783, Snmp.Valor(Respuesta(7, 0, 0x41, [0x00, 0xF1, 0x57]), 7));
        Assert.Null(Snmp.Valor(Respuesta(8, 0, 0x41, [0x00, 0xF1, 0x57]), 7)); // de otra petición
        Assert.Null(Snmp.Valor(Respuesta(7, 2, 0x41, [0x00, 0xF1, 0x57]), 7)); // noSuchName
        Assert.Null(Snmp.Valor(Respuesta(7, 0, 0x81, []), 7)); // noSuchInstance: no tiene contador
        Assert.Null(Snmp.Valor([0x30, 0x05, 0x02], 7)); // cortada
    }

    // Trozo real de «Revisar contadores» de una Canon imageFORCE 1643 (10/11/2026).
    [Fact]
    public void ContadorCanon_LeeElTotal113()
    {
        const string html = """
            write_index("113");
            write_type(113);
            write_value("113",11090);
            write_index("501");
            write_value("501",120);
            write_value("301",10942);
            """;
        Assert.Equal(11090, LectorContador.ContadorCanon(html));
        Assert.Null(LectorContador.ContadorCanon("<title>Remote UI : Cannot open this page.</title>"));
    }

    // Trozo real de «Consumibles» de una Canon (10/11/2026).
    [Fact]
    public void DiasTonerCanon_LeeLosDiasQueEstimaLaImpresora()
    {
        Assert.Equal(15, LectorContador.DiasTonerCanon(
            """var tonerList = [ {item:"TONER_K", type:"Toner T06", stat:"14", statInd:"TWENTY_PERCENT", days:"15", daysKind:"WITH_IN_YEAR"} ];"""));
        Assert.Equal(LecturaContador.MasDeUnAno, LectorContador.DiasTonerCanon(
            """var tonerList = [ {item:"TONER_K", type:"Toner T06", stat:"98", statInd:"HUNDRED_PERCENT", days:"999", daysKind:"ONE_YEAR_OVER"} ];"""));
        Assert.Null(LectorContador.DiasTonerCanon("""var tonerList = [ {item:"TONER_K", type:"Toner T06", stat:"98", statInd:"HUNDRED_PERCENT", days:"", daysKind:""} ];"""));
    }

    private static PageCounterReading L(int dia, long total, int hora = 0) => new()
    {
        PrinterId = 1,
        BusinessDay = Dia.AddDays(dia),
        TotalPages = total,
        Source = LectorContador.OrigenCanon,
        ReadAtUtc = new DateTimeOffset(Dia.AddDays(dia).ToDateTime(new TimeOnly(hora, 0)), TimeSpan.Zero)
    };

    [Fact]
    public void Periodos_CadaDiaEsLaLecturaDelSiguienteMenosLaSuya()
    {
        // Del 10 al 12: lecturas de los días 10, 11, 12 y 13 (la del 13 cierra el 12).
        var p = ContadorPaginas.Periodos([L(-1, 50), L(0, 100), L(1, 130), L(2, 175), L(3, 180), L(4, 999)],
            Dia, Dia.AddDays(2));

        Assert.Equal([30L, 45L, 5L], p.Select(x => x.Paginas!.Value));
        Assert.Equal(Dia, p[0].Desde);
        Assert.Equal(Dia.AddDays(3), p[^1].Hasta);
    }

    [Fact]
    public void Periodos_SinLecturaDeUnDia_ElPeriodoAbarcaMasDias()
    {
        // Falta la del 10 (inicio) y la del 12: se toma la del 9 y el 11 llega hasta el 13.
        var p = ContadorPaginas.Periodos([L(-1, 50), L(1, 130), L(3, 180)], Dia, Dia.AddDays(2));

        Assert.Equal(2, p.Count);
        Assert.Equal((Dia.AddDays(-1), Dia.AddDays(1), 80L), (p[0].Desde, p[0].Hasta, p[0].Paginas!.Value));
        Assert.Equal((Dia.AddDays(1), Dia.AddDays(3), 50L), (p[1].Desde, p[1].Hasta, p[1].Paginas!.Value));
    }

    [Fact]
    public void Periodos_SiElContadorBaja_NoSeSabenEsasPaginas()
    {
        var p = ContadorPaginas.Periodos([L(0, 9000), L(1, 20), L(2, 50)], Dia, Dia.AddDays(1));

        Assert.Null(p[0].Paginas);
        Assert.Equal(30, p[1].Paginas);
    }

    [Fact]
    public void Periodos_SinLecturasDeHoy_NoHayPeriodo()
        => Assert.Empty(ContadorPaginas.Periodos([L(0, 100)], Dia, Dia));

    private sealed class LectorFalso(Func<Printer, int, LecturaContador?> responde) : ILectorContador
    {
        public Dictionary<int, int> Intentos { get; } = [];

        public Task<LecturaContador?> LeerAsync(Printer impresora, CancellationToken ct)
        {
            var n = Intentos[impresora.PrinterId] = Intentos.GetValueOrDefault(impresora.PrinterId) + 1;
            return Task.FromResult(responde(impresora, n));
        }
    }

    [Fact]
    public async Task TomarAsync_GuardaUnaLecturaPorImpresoraYDia_YReintentaAlMomento()
    {
        using var setup = SqliteTestDbHelper.CreateOpenSqliteInMemory();
        var db = setup.Db;
        Printer P(int id, string? host, bool activa = true) => new()
        {
            PrinterId = id, PrinterName = $"P{id}", Host = host, StoreId = 1, IsActive = activa,
            CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Stores.Add(new Store { StoreId = 1, Name = "Tienda", IsActive = true });
        db.Printers.AddRange(P(1, "10.0.0.1"), P(2, "10.0.0.2"), P(3, "10.0.0.3"), P(4, null), P(5, "10.0.0.5", activa: false));
        await db.SaveChangesAsync();

        // La 1 responde; la 2, al segundo intento; la 3, nunca.
        var lector = new LectorFalso((p, n) => p.PrinterId switch
        {
            1 => new(1000, LectorContador.OrigenCanon),
            2 when n >= 2 => new(2000, LectorContador.OrigenSnmp),
            _ => null
        });
        var reloj = new ManualTimeProvider();
        reloj.SetUtcNow(new DateTimeOffset(2026, 11, 9, 23, 0, 5, TimeSpan.Zero));

        var sinLectura = await ContadorPaginas.TomarAsync(db, lector, Dia, reloj, CancellationToken.None);

        Assert.Equal([3], sinLectura.Select(p => p.PrinterId));
        Assert.Equal(2, lector.Intentos[3]); // una vez más al momento
        Assert.False(lector.Intentos.ContainsKey(4)); // sin IP
        Assert.False(lector.Intentos.ContainsKey(5)); // inactiva
        var guardadas = await db.PageCounterReadings.AsNoTracking().OrderBy(r => r.PrinterId).ToListAsync();
        Assert.Equal([(1, 1000L, "canon-113"), (2, 2000L, "snmp")],
            guardadas.Select(r => (r.PrinterId, r.TotalPages, r.Source)));
        Assert.All(guardadas, r => Assert.Equal(Dia, r.BusinessDay));

        // La siguiente pasada solo vuelve a la que falta.
        await ContadorPaginas.TomarAsync(db, lector, Dia, reloj, CancellationToken.None);
        Assert.Equal(1, lector.Intentos[1]);
        Assert.Equal(4, lector.Intentos[3]);
        Assert.Equal(2, await db.PageCounterReadings.CountAsync());
    }
}
