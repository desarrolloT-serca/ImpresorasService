using System.Net;
using System.Text.Json;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

/// <summary>GET /api/printers/page-counts: páginas por impresora y rango (ERP_SERCA docs/73, opción E).</summary>
public sealed class PaginasImpresasTests : IntegrationTestBase
{
    public PaginasImpresasTests(ApiWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task DaLasPaginasDeCadaImpresoraEnElRango()
    {
        await Factory.ResetDatabaseAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            var ahora = DateTimeOffset.UtcNow;
            Printer P(string nombre, bool activa = true) => new()
            {
                PrinterName = nombre, Host = "10.9.9.9", StoreId = 1, IsActive = activa,
                CreatedAtUtc = ahora, UpdatedAtUtc = ahora
            };
            var conLecturas = P("MOSTRADOR01");
            var sinLecturas = P("ALMACEN01");
            var inactiva = P("VIEJA", activa: false);
            db.Printers.AddRange(conLecturas, sinLecturas, inactiva);
            await db.SaveChangesAsync();
            db.PageCounterReadings.AddRange(new[] { (1, 100L), (2, 130L), (3, 175L) }.Select(x => new PageCounterReading
            {
                PrinterId = conLecturas.PrinterId,
                BusinessDay = new DateOnly(2026, 11, x.Item1),
                TotalPages = x.Item2,
                Source = "canon-113",
                ReadAtUtc = ahora
            }));
            await db.SaveChangesAsync();
        }

        var r = await Client.GetAsync("/api/printers/page-counts?from=2026-11-01&to=2026-11-02");
        r.EnsureSuccessStatusCode();
        var lista = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

        // La inactiva sin lecturas no sale; la activa sin lecturas sí, sin páginas.
        Assert.Equal(["ALMACEN01", "MOSTRADOR01"], lista.EnumerateArray().Select(i => i.GetProperty("printerName").GetString()));
        Assert.Equal(JsonValueKind.Null, lista[0].GetProperty("pages").ValueKind);
        var mostrador = lista[1];
        Assert.Equal(75, mostrador.GetProperty("pages").GetInt64());
        Assert.Equal("canon-113", mostrador.GetProperty("source").GetString());
        var dias = mostrador.GetProperty("periods");
        Assert.Equal(2, dias.GetArrayLength());
        Assert.Equal("2026-11-01", dias[0].GetProperty("from").GetString());
        Assert.Equal("2026-11-02", dias[0].GetProperty("to").GetString());
        Assert.Equal(30, dias[0].GetProperty("pages").GetInt64());
    }

    // docs/72: en las Canon, los días de tóner son los que estima la impresora (leídos con el contador).
    [Fact]
    public async Task LaFichaDaLosDiasDeTonerDeLaCanon()
    {
        int id;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            var ahora = DateTimeOffset.UtcNow;
            var p = new Printer
            {
                PrinterName = $"CANON-{Guid.NewGuid():N}"[..20], Host = "10.9.9.8", StoreId = 1,
                CreatedAtUtc = ahora, UpdatedAtUtc = ahora
            };
            db.Printers.Add(p);
            await db.SaveChangesAsync();
            id = p.PrinterId;
            db.PageCounterReadings.Add(new PageCounterReading
            {
                PrinterId = id, BusinessDay = DateOnly.FromDateTime(ahora.UtcDateTime), TotalPages = 1,
                Source = "canon-113", TonerDaysLeft = 15, ReadAtUtc = ahora.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        var dto = JsonDocument.Parse(await Client.GetStringAsync($"/api/printers/{id}")).RootElement;

        Assert.Equal(15, dto.GetProperty("tonerDaysLeft").GetInt32());
        Assert.Equal("impresora", dto.GetProperty("tonerDaysSource").GetString());
    }

    [Theory]
    [InlineData("from=2026-11-02&to=2026-11-01")]
    [InlineData("from=2026-11-01")]
    [InlineData("from=2024-01-01&to=2026-11-01")]
    public async Task RechazaRangosNoValidos(string consulta)
        => Assert.Equal(HttpStatusCode.BadRequest,
            (await Client.GetAsync($"/api/printers/page-counts?{consulta}")).StatusCode);
}
