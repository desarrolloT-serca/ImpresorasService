using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ImpresorasService.Infrastructure.Services;

/// <summary>
/// Token del bot que se usa: el puesto desde la pantalla (printer_telegram_config.bot_token) o, si no
/// hay, Telegram:BotToken de la config. Se lee en cada envío: cambiarlo no pide reiniciar el Worker.
/// </summary>
public static class TelegramToken
{
    public static async Task<string> ResolverAsync(ImpresorasDbContext db, TelegramOptions options, CancellationToken ct)
    {
        var guardado = await db.TelegramConfigs.AsNoTracking()
            .Where(x => x.Id == 1)
            .Select(x => x.BotToken)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(guardado) ? options.BotToken : guardado;
    }
}
