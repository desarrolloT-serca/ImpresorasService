using System.Text;
using System.Text.Json;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Infrastructure.Services;

internal sealed class TelegramNotifierService : ITelegramNotifier, IDisposable
{
    private readonly IOptions<TelegramOptions> _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramNotifierService> _logger;
    private readonly HttpClient _http;

    public TelegramNotifierService(
        IOptions<TelegramOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramNotifierService> logger)
    {
        _options = options;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task SendAlertAsync(string message, CancellationToken ct, int? storeId = null)
    {
        if (!_options.Value.Enabled || string.IsNullOrWhiteSpace(_options.Value.BotToken))
        {
            _logger.LogDebug("Telegram desactivado o sin token. Mensaje omitido.");
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpresorasDbContext>();

        var allChats = await db.TelegramChats.AsNoTracking().ToListAsync(ct);
        var chatIds = allChats
            .Where(c => c.IsActive && (c.StoreId == null || c.StoreId == storeId))
            .Select(c => c.ChatId)
            .ToList();

        if (chatIds.Count == 0)
        {
            _logger.LogDebug("No hay chats de Telegram activos registrados.");
            return;
        }

        var url = $"https://api.telegram.org/bot{_options.Value.BotToken}/sendMessage";

        foreach (var chatId in chatIds)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    chat_id = chatId,
                    text = message,
                    parse_mode = "HTML"
                });

                var response = await _http.PostAsync(url,
                    new StringContent(payload, Encoding.UTF8, "application/json"), ct);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("Telegram rechazó mensaje a chat {ChatId}: {Status} — {Body}",
                        chatId, (int)response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error enviando alerta Telegram a chat {ChatId}.", chatId);
            }
        }
    }

    public void Dispose() => _http.Dispose();
}
