using System.Net;
using System.Net.Http.Json;
using ImpresorasService.Api.Controllers;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

/// <summary>
/// Franjas de la gráfica del dashboard: automáticas según el periodo o las que se pidan, alineadas a la
/// hora de Madrid. Sin bucketMinutes, como siempre (el PHP).
/// </summary>
public sealed class DashboardFranjasTests : IClassFixture<WindowClockApiFactory>
{
    private readonly WindowClockApiFactory _factory;
    private readonly HttpClient _client;

    public DashboardFranjasTests(WindowClockApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData(0, 2 * 60, 15)]           // hoy a las 2:00: tramos de 15 min
    [InlineData(0, 16 * 60, 30)]          // hoy a las 16:00
    [InlineData(0, 7 * 24 * 60, 240)]     // 7 días: 4 h
    [InlineData(0, 30 * 24 * 60, 1440)]   // 30 días: un día
    [InlineData(60, 16 * 60, 60)]         // la que se pida
    [InlineData(15, 30 * 24 * 60, null)]  // demasiados tramos
    [InlineData(45, 16 * 60, null)]       // no es una de las franjas
    public void Elige_la_franja(int pedida, int minutosDePeriodo, int? esperada)
        => Assert.Equal(esperada, DashboardController.ElegirFranja(pedida, TimeSpan.FromMinutes(minutosDePeriodo)));

    [Fact]
    public async Task Automatica_y_alineada_a_la_hora_de_Madrid()
    {
        // 26/10/2026 16:23 en Madrid (CET, UTC+1).
        _factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 26, 15, 23, 0, TimeSpan.Zero));

        var hoy = await SerieAsync("today", 0);
        var semana = await SerieAsync("7d", 240);

        Assert.Equal(30, hoy.BucketMinutes);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), hoy.Points[0].StartUtc); // 00:00 en Madrid
        Assert.Equal(240, semana.BucketMinutes);
        Assert.Equal(0, MadridHora(semana.Points[0].StartUtc) % 4); // 00, 04, 08… no 16:23
        Assert.Equal(0, semana.Points[0].StartUtc.Minute);
    }

    [Fact]
    public async Task Con_cambio_de_hora_los_dias_siguen_empezando_a_las_00()
    {
        // 26/10/2026 16:23 en Madrid; el 25/10 se pasó de CEST (UTC+2) a CET (UTC+1).
        _factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 26, 15, 23, 0, TimeSpan.Zero));

        var dias = await SerieAsync("7d", 1440);
        var horas = await SerieAsync("7d", 240);

        Assert.All(dias.Points, p => Assert.Equal(0, MadridHora(p.StartUtc)));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), dias.Points[^1].StartUtc); // 26/10 00:00
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero), dias.Points[^2].StartUtc); // 25/10 00:00
        Assert.All(horas.Points, p => Assert.Equal(0, MadridHora(p.StartUtc) % 4));
    }

    [Fact]
    public async Task Sin_franja_responde_como_siempre_y_una_mala_da_400()
    {
        _factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 26, 15, 23, 0, TimeSpan.Zero));

        Assert.Equal(60, (await SerieAsync("today", null)).BucketMinutes);
        Assert.Equal(1440, (await SerieAsync("30d", null)).BucketMinutes);
        var mala = await _client.GetAsync("/api/dashboard/timeseries?window=30d&bucketMinutes=15");
        Assert.Equal(HttpStatusCode.BadRequest, mala.StatusCode);
    }

    private static int MadridHora(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid")).Hour;

    private async Task<Serie> SerieAsync(string ventana, int? franja)
    {
        var url = $"/api/dashboard/timeseries?window={ventana}" + (franja is null ? "" : $"&bucketMinutes={franja}");
        var r = await _client.GetAsync(url);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<Serie>())!;
    }

    private sealed record Serie(string Bucket, int BucketMinutes, List<Punto> Points);
    private sealed record Punto(DateTimeOffset StartUtc);
}
