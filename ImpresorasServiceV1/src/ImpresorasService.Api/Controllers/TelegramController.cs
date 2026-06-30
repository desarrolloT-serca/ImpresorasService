using System.Text;
using System.Text.Json;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ImpresorasService.Api.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/telegram")]
public class TelegramController : ControllerBase
{
    private readonly ImpresorasDbContext _db;
    private readonly ITelegramNotifier _notifier;
    private readonly IOptions<TelegramOptions> _telegramOptions;

    public TelegramController(ImpresorasDbContext db, ITelegramNotifier notifier, IOptions<TelegramOptions> telegramOptions)
    {
        _db = db;
        _notifier = notifier;
        _telegramOptions = telegramOptions;
    }

    // --- Config ---

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(CancellationToken ct)
    {
        var cfg = await _db.TelegramConfigs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, ct);

        if (cfg is null)
            return Ok(new { enabled = false, minSeverity = "critical", notifyOnRecovery = true, checkIntervalMinutes = 5 });

        return Ok(new
        {
            cfg.Enabled,
            cfg.MinSeverity,
            cfg.NotifyOnRecovery,
            cfg.CheckIntervalMinutes,
            cfg.UpdatedAtUtc
        });
    }

    [HttpPut("config")]
    public async Task<IActionResult> PutConfig([FromBody] PutConfigRequest req, CancellationToken ct)
    {
        if (!new[] { "warning", "critical" }.Contains(req.MinSeverity?.ToLowerInvariant()))
            return BadRequest(new { error = "minSeverity debe ser 'warning' o 'critical'." });

        if (req.CheckIntervalMinutes < 1)
            return BadRequest(new { error = "checkIntervalMinutes debe ser >= 1." });

        var cfg = await _db.TelegramConfigs.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (cfg is null)
        {
            cfg = new TelegramConfig { Id = 1 };
            await _db.TelegramConfigs.AddAsync(cfg, ct);
        }

        cfg.Enabled = req.Enabled;
        cfg.MinSeverity = req.MinSeverity!.ToLowerInvariant();
        cfg.NotifyOnRecovery = req.NotifyOnRecovery;
        cfg.CheckIntervalMinutes = req.CheckIntervalMinutes;
        cfg.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --- Chats ---

    [HttpGet("chats")]
    public async Task<IActionResult> GetChats(CancellationToken ct)
    {
        var chats = await _db.TelegramChats.AsNoTracking()
            .OrderBy(c => c.ChatId)
            .Select(c => new { c.ChatId, c.Description, c.IsActive, c.StoreId, c.CreatedAtUtc })
            .ToListAsync(ct);

        return Ok(chats);
    }

    [HttpPost("chats")]
    public async Task<IActionResult> AddChat([FromBody] AddChatRequest req, CancellationToken ct)
    {
        if (req.ChatId == 0)
            return BadRequest(new { error = "chatId es obligatorio y no puede ser 0." });

        if (req.StoreId.HasValue)
        {
            var storeExists = await _db.Stores.AsNoTracking()
                .FirstOrDefaultAsync(s => s.StoreId == req.StoreId.Value, ct) is not null;
            if (!storeExists)
                return BadRequest(new { error = $"La tienda {req.StoreId} no existe." });
        }

        var exists = await _db.TelegramChats.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChatId == req.ChatId, ct) is not null;
        if (exists)
            return Conflict(new { error = $"El chat {req.ChatId} ya existe." });

        var chat = new TelegramChat
        {
            ChatId = req.ChatId,
            Description = req.Description?.Trim(),
            IsActive = true,
            StoreId = req.StoreId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await _db.TelegramChats.AddAsync(chat, ct);
        await _db.SaveChangesAsync(ct);

        return Created($"api/telegram/chats/{chat.ChatId}", new { chat.ChatId, chat.Description, chat.IsActive, chat.CreatedAtUtc });
    }

    [HttpDelete("chats/{chatId:long}")]
    public async Task<IActionResult> DeleteChat(long chatId, CancellationToken ct)
    {
        var chat = await _db.TelegramChats.SingleOrDefaultAsync(c => c.ChatId == chatId, ct);
        if (chat is null)
            return NotFound(new { error = $"Chat {chatId} no encontrado." });

        _db.TelegramChats.Remove(chat);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // --- Test ---

    [HttpPost("test")]
    public async Task<IActionResult> SendTest(CancellationToken ct)
    {
        var allChats = await _db.TelegramChats.AsNoTracking().ToListAsync(ct);
        var activeChats = allChats.Where(c => c.IsActive).ToList();
        if (activeChats.Count == 0)
            return BadRequest(new { error = "No hay chats activos registrados para enviar el mensaje de prueba." });

        var token = _telegramOptions.Value.BotToken;
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { error = "Bot token no configurado." });

        var url = $"https://api.telegram.org/bot{token}/sendMessage";
        var results = new List<object>();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        foreach (var chat in activeChats)
        {
            var payload = JsonSerializer.Serialize(new
            {
                chat_id = chat.ChatId,
                text = "<b>IMPRESORAS SERVICE</b>  ·  <i>Prueba</i>\n\nSistema de alertas operativo.",
                parse_mode = "HTML"
            });

            try
            {
                var resp = await http.PostAsync(url,
                    new StringContent(payload, Encoding.UTF8, "application/json"), ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                results.Add(new { chatId = chat.ChatId, status = (int)resp.StatusCode, ok = resp.IsSuccessStatusCode, telegramResponse = body });
            }
            catch (Exception ex)
            {
                results.Add(new { chatId = chat.ChatId, status = 0, ok = false, telegramResponse = ex.Message });
            }
        }

        return Ok(new { results });
    }

    // --- DTOs ---

    public record PutConfigRequest(bool Enabled, string? MinSeverity, bool NotifyOnRecovery, int CheckIntervalMinutes);
    public record AddChatRequest(long ChatId, string? Description, int? StoreId);
}
