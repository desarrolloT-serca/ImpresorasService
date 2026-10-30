using System.Net;
using System.Net.Http.Json;
using ImpresorasService.Application.Services;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImpresorasService.Api.IntegrationTests.Controllers;

/// <summary>
/// Pantalla de Telegram (27/10/2026): editar y pausar un chat sin borrarlo, probar uno solo y ver
/// el aviso tal como llega (el mismo texto que manda el Worker).
/// </summary>
public sealed class TelegramControllerTests : IntegrationTestBase
{
    public TelegramControllerTests(ApiWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task UpdateChat_CambiaDescripcionYLoPausaSinBorrarlo()
    {
        const long chatId = -100777;
        await Client.DeleteAsync($"/api/telegram/chats/{chatId}");
        (await Client.PostAsJsonAsync("/api/telegram/chats", new { chatId, description = "Antes" })).EnsureSuccessStatusCode();

        var put = await Client.PutAsJsonAsync($"/api/telegram/chats/{chatId}",
            new { description = "  Jefes de tienda  ", storeId = (int?)null, isActive = false });

        put.EnsureSuccessStatusCode();
        var chats = await Client.GetFromJsonAsync<List<ChatDto>>("/api/telegram/chats");
        var chat = Assert.Single(chats!, c => c.ChatId == chatId);
        Assert.Equal("Jefes de tienda", chat.Description);
        Assert.False(chat.IsActive);
    }

    [Fact]
    public async Task UpdateChat_Inexistente_O_TiendaQueNoExiste_SeRechaza()
    {
        var noExiste = await Client.PutAsJsonAsync("/api/telegram/chats/-1009999", new { description = "x", isActive = true });
        Assert.Equal(HttpStatusCode.NotFound, noExiste.StatusCode);

        const long chatId = -100778;
        await Client.DeleteAsync($"/api/telegram/chats/{chatId}");
        (await Client.PostAsJsonAsync("/api/telegram/chats", new { chatId })).EnsureSuccessStatusCode();
        var tiendaMala = await Client.PutAsJsonAsync($"/api/telegram/chats/{chatId}", new { storeId = 987654, isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, tiendaMala.StatusCode);
    }

    [Fact]
    public async Task Prueba_AUnChatQueNoExiste_Da404()
    {
        var r = await Client.PostAsync("/api/telegram/test?chatId=-1005555", null);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task VistaPrevia_EsElMismoTextoQueManda()
    {
        var r = await Client.GetFromJsonAsync<VistaPreviaDto>("/api/telegram/preview");

        Assert.Contains("<b>CRÍTICA</b>", r!.Alerta);
        Assert.Contains("<b>RECUPERADA</b>", r.Recuperada);
    }

    [Fact]
    public void Mensajes_EscapanElHtmlDelNombreYElMotivo()
    {
        // Un «&» sin escapar hacía que Telegram rechazara el mensaje (parse_mode HTML).
        var texto = TelegramMensajes.Alerta("A & B", 1, "warning", "Cola <5>", 1, 0, "27/10 10:00");

        Assert.Contains("<b>A &amp; B</b>", texto);
        Assert.Contains("Cola &lt;5&gt;", texto);
    }

    /// <summary>
    /// «Solo en horario de tienda» se guarda con las reglas de umbrales (sin tocar la BD) y un cliente
    /// que no lo manda (el PHP) no lo borra. Se deja como estaba: el fichero de reglas es compartido.
    /// </summary>
    [Fact]
    public async Task SoloEnHorario_SeGuardaYUnClienteAnteriorNoLoBorra()
    {
        try
        {
            (await Client.PutAsJsonAsync("/api/telegram/config",
                new { minSeverity = "critical", notifyOnRecovery = true, checkIntervalMinutes = 5, soloEnHorario = true }))
                .EnsureSuccessStatusCode();
            (await Client.PutAsJsonAsync("/api/telegram/config",
                new { minSeverity = "critical", notifyOnRecovery = true, checkIntervalMinutes = 5 }))
                .EnsureSuccessStatusCode();

            var config = await Client.GetFromJsonAsync<ConfigDto>("/api/telegram/config");
            Assert.True(config!.SoloEnHorario);
            Assert.NotEmpty(config.Horario.Dias);
        }
        finally
        {
            (await Client.PutAsJsonAsync("/api/telegram/config",
                new { minSeverity = "critical", notifyOnRecovery = true, checkIntervalMinutes = 5, soloEnHorario = false }))
                .EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// Token puesto desde la pantalla: la API nunca lo devuelve (solo de dónde viene y sus 4 últimos),
    /// rechaza lo que no tiene forma de token sin preguntar a Telegram y, vacío, lo quita.
    /// </summary>
    [Fact]
    public async Task Token_NoSeDevuelveYVacioLoQuita()
    {
        const string token = "123456789:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwxyz";
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            var cfg = await db.TelegramConfigs.SingleOrDefaultAsync(x => x.Id == 1);
            if (cfg is null)
                db.TelegramConfigs.Add(cfg = new TelegramConfig { Id = 1 });
            cfg.BotToken = token;
            await db.SaveChangesAsync();
        }

        var texto = await Client.GetStringAsync("/api/telegram/config");
        Assert.DoesNotContain(token, texto);
        var config = await Client.GetFromJsonAsync<ConfigDto>("/api/telegram/config");
        Assert.Equal("pantalla", config!.TokenOrigen);
        Assert.Equal("wxyz", config.TokenFinal);

        var malo = await Client.PutAsJsonAsync("/api/telegram/token", new { botToken = "t.me/+abc" });
        Assert.Equal(HttpStatusCode.BadRequest, malo.StatusCode);

        // Sin botToken no se borra: quitarlo es mandarlo vacío.
        var sinToken = await Client.PutAsJsonAsync("/api/telegram/token", new { });
        Assert.Equal(HttpStatusCode.BadRequest, sinToken.StatusCode);
        config = await Client.GetFromJsonAsync<ConfigDto>("/api/telegram/config");
        Assert.Equal("pantalla", config!.TokenOrigen);

        (await Client.PutAsJsonAsync("/api/telegram/token", new { botToken = "" })).EnsureSuccessStatusCode();
        config = await Client.GetFromJsonAsync<ConfigDto>("/api/telegram/config");
        Assert.NotEqual("pantalla", config!.TokenOrigen);
    }

    /// <summary>
    /// Historial sin tabla propia: eventos TELEGRAM_ALERT con un job_id fijo por tienda, que ninguna
    /// consulta de trabajos ve. Lo más reciente primero, con la tienda y si llegó.
    /// </summary>
    [Fact]
    public async Task Historial_LeeLosAvisosDeLasTiendas()
    {
        const int storeId = 974;
        var ahora = DateTimeOffset.UtcNow;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();
            if (!await db.Stores.AnyAsync(s => s.StoreId == storeId))
                db.Stores.Add(new Store { StoreId = storeId, Name = "Tienda Historial", IsActive = true, CreatedAtUtc = ahora, UpdatedAtUtc = ahora });
            var jobId = TelegramMensajes.JobIdHistorial(storeId);
            db.PrintJobEvents.RemoveRange(await db.PrintJobEvents.Where(e => e.JobId == jobId).ToListAsync());
            db.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = jobId, EventType = TelegramMensajes.EventoHistorial, ActorType = "system",
                Message = "Cola actual mayor o igual a 20 trabajos", OccurredAtUtc = ahora.AddMinutes(-10),
                MetadataJson = $$"""{"storeId":{{storeId}},"health":"critical","delivered":true}"""
            });
            db.PrintJobEvents.Add(new PrintJobEvent
            {
                JobId = jobId, EventType = TelegramMensajes.EventoHistorial, ActorType = "system",
                Message = "Recuperada (venía de critical)", OccurredAtUtc = ahora,
                MetadataJson = $$"""{"storeId":{{storeId}},"health":"healthy","delivered":false}"""
            });
            await db.SaveChangesAsync();
        }

        var historial = await Client.GetFromJsonAsync<List<AvisoDto>>("/api/telegram/history?limit=200");

        var deLaTienda = historial!.Where(h => h.StoreId == storeId).ToList();
        Assert.Equal(2, deLaTienda.Count);
        Assert.Equal(("healthy", false), (deLaTienda[0].Health, deLaTienda[0].Delivered));
        Assert.Equal(("critical", true), (deLaTienda[1].Health, deLaTienda[1].Delivered));
        Assert.Equal("Tienda Historial", deLaTienda[0].StoreName);
    }

    [Fact]
    public void JobIdHistorial_EsFijoPorTiendaYDistintoEntreTiendas()
    {
        Assert.Equal(TelegramMensajes.JobIdHistorial(12), TelegramMensajes.JobIdHistorial(12));
        Assert.NotEqual(TelegramMensajes.JobIdHistorial(12), TelegramMensajes.JobIdHistorial(13));
    }

    private sealed record HorarioDto(int[] Dias, string Desde, string Hasta);
    private sealed record ConfigDto(bool SoloEnHorario, HorarioDto Horario, string? TokenOrigen, string? TokenFinal);
    private sealed record AvisoDto(int? StoreId, string? StoreName, string Health, string? Reason, bool Delivered);
    private sealed record ChatDto(long ChatId, string? Description, bool IsActive, int? StoreId);
    private sealed record VistaPreviaDto(string Alerta, string Recuperada);
}
