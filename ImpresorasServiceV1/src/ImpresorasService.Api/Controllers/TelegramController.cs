using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ImpresorasService.Application.Abstractions;
using ImpresorasService.Domain.Entities;
using ImpresorasService.Infrastructure.Options;
using ImpresorasService.Infrastructure.Persistence;
using ImpresorasService.Infrastructure.Services;
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
    private readonly IOptions<TelegramOptions> _telegramOptions;

    // Sin ITelegramNotifier: este controlador solo administra la configuración y los chats. Quien
    // envía es el Worker (StoreHealthAlertBackgroundService); la dependencia estaba inyectada y sin
    // usar desde que se retiró el envío de prueba.
    // Las reglas de umbrales guardan el horario de tienda y si Telegram solo avisa dentro de él.
    private readonly IDashboardThresholdRuleStore _ruleStore;

    public TelegramController(ImpresorasDbContext db, IOptions<TelegramOptions> telegramOptions,
        IDashboardThresholdRuleStore ruleStore)
    {
        _db = db;
        _telegramOptions = telegramOptions;
        _ruleStore = ruleStore;
    }

    // --- Config ---

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(CancellationToken ct)
    {
        var cfg = await _db.TelegramConfigs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, ct);
        var reglas = await _ruleStore.LoadAsync(ct);
        // Solo en horario de tienda: va con las reglas de umbrales, junto al horario (sin tocar la BD).
        var horario = new { dias = reglas.Horario.Dias, desde = reglas.Horario.Desde, hasta = reglas.Horario.Hasta };
        // El token nunca sale de aquí: solo de dónde viene y sus 4 últimos caracteres, para reconocerlo.
        var tokenOrigen = !string.IsNullOrWhiteSpace(cfg?.BotToken) ? "pantalla"
            : !string.IsNullOrWhiteSpace(_telegramOptions.Value.BotToken) ? "servidor" : null;
        var token = tokenOrigen == "pantalla" ? cfg!.BotToken! : _telegramOptions.Value.BotToken;
        var tokenFinal = tokenOrigen is null ? null : token[^Math.Min(4, token.Length)..];

        if (cfg is null)
            return Ok(new
            {
                enabled = _telegramOptions.Value.Enabled, minSeverity = "critical", notifyOnRecovery = true,
                checkIntervalMinutes = 5, soloEnHorario = reglas.TelegramSoloEnHorario, horario,
                tokenOrigen, tokenFinal
            });

        return Ok(new
        {
            enabled = _telegramOptions.Value.Enabled,
            cfg.MinSeverity,
            cfg.NotifyOnRecovery,
            cfg.CheckIntervalMinutes,
            cfg.UpdatedAtUtc,
            soloEnHorario = reglas.TelegramSoloEnHorario,
            horario,
            tokenOrigen,
            tokenFinal
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

        cfg.MinSeverity = req.MinSeverity!.ToLowerInvariant();
        cfg.NotifyOnRecovery = req.NotifyOnRecovery;
        cfg.CheckIntervalMinutes = req.CheckIntervalMinutes;
        cfg.UpdatedAtUtc = DateTimeOffset.UtcNow;

        // Un cliente que no lo manda (el PHP) lo deja como está.
        if (req.SoloEnHorario is bool soloEnHorario)
        {
            var reglas = await _ruleStore.LoadAsync(ct);
            if (reglas.TelegramSoloEnHorario != soloEnHorario)
            {
                var errores = await _ruleStore.SaveAsync(reglas with { TelegramSoloEnHorario = soloEnHorario }, ct);
                if (errores.Count > 0)
                    return BadRequest(new { errors = errores });
            }
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Pone el token del bot (manda sobre Telegram:BotToken del servicio) o, vacío, lo quita y vuelve el
    /// del servicio. Quitarlo es mandar el texto vacío, no omitirlo: un cuerpo sin botToken da 400 y no
    /// lo borra. Antes de guardarlo se comprueba con Telegram (getMe): uno mal copiado dejaría sin
    /// avisos sin que nadie se enterase. Devuelve el usuario del bot para que se vea que es el bueno.
    /// </summary>
    [HttpPut("token")]
    public async Task<IActionResult> PutToken([FromBody] PutTokenRequest req, CancellationToken ct)
    {
        if (req.BotToken is null)
            return BadRequest(new { error = "Falta botToken (vacío para quitar el de la pantalla)." });
        var token = req.BotToken.Trim();
        string? bot = null;
        if (!string.IsNullOrEmpty(token))
        {
            if (!Regex.IsMatch(token, @"^\d{5,15}:[A-Za-z0-9_-]{30,80}$"))
                return BadRequest(new { error = "No parece un token de bot: es un número, dos puntos y unas 35 letras (te lo da @BotFather)." });

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            try
            {
                var resp = await http.GetAsync($"https://api.telegram.org/bot{token}/getMe", ct);
                if (!resp.IsSuccessStatusCode)
                    return BadRequest(new { error = "Telegram no reconoce ese token. Cópialo otra vez de @BotFather." });
                using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                bot = json.RootElement.GetProperty("result").GetProperty("username").GetString();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return StatusCode(502, new { error = "No se ha podido hablar con Telegram para comprobar el token. Prueba otra vez." });
            }
        }

        var cfg = await _db.TelegramConfigs.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (cfg is null)
        {
            cfg = new TelegramConfig { Id = 1 };
            await _db.TelegramConfigs.AddAsync(cfg, ct);
        }
        cfg.BotToken = string.IsNullOrEmpty(token) ? null : token;
        cfg.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { bot });
    }

    /// <summary>
    /// Historial de avisos mandados (o que no llegaron), lo más reciente primero. Son eventos
    /// TELEGRAM_ALERT de printer_print_job_event, con un job_id fijo por tienda (TelegramMensajes).
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int? limit, CancellationToken ct)
    {
        var tiendas = await _db.Stores.AsNoTracking()
            .Select(s => new { s.StoreId, s.Name })
            .ToDictionaryAsync(s => s.StoreId, s => s.Name, ct);
        var ids = tiendas.Keys.Select(Application.Services.TelegramMensajes.JobIdHistorial).ToList();
        var eventos = await _db.PrintJobEvents.AsNoTracking()
            .Where(e => ids.Contains(e.JobId) && e.EventType == Application.Services.TelegramMensajes.EventoHistorial)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(Math.Clamp(limit ?? 50, 1, 200))
            .Select(e => new { e.OccurredAtUtc, e.Message, e.MetadataJson })
            .ToListAsync(ct);

        return Ok(eventos.Select(e =>
        {
            var datos = JsonSerializer.Deserialize<DatosHistorial>(e.MetadataJson ?? "{}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return new
            {
                e.OccurredAtUtc,
                storeId = datos?.StoreId,
                storeName = datos?.StoreId is int id ? tiendas.GetValueOrDefault(id) : null,
                health = datos?.Health ?? "warning",
                reason = e.Message,
                delivered = datos?.Delivered ?? false
            };
        }));
    }

    private sealed record DatosHistorial(int? StoreId, string? Health, bool? Delivered);

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

    /// <summary>
    /// Editar un chat: descripción, tienda y si está activo (pausarlo sin perderlo). El id no cambia:
    /// para otro chat, se añade uno nuevo.
    /// </summary>
    [HttpPut("chats/{chatId:long}")]
    public async Task<IActionResult> UpdateChat(long chatId, [FromBody] UpdateChatRequest req, CancellationToken ct)
    {
        var chat = await _db.TelegramChats.SingleOrDefaultAsync(c => c.ChatId == chatId, ct);
        if (chat is null)
            return NotFound(new { error = $"Chat {chatId} no encontrado." });
        if (req.StoreId.HasValue && !await _db.Stores.AsNoTracking().AnyAsync(s => s.StoreId == req.StoreId.Value, ct))
            return BadRequest(new { error = $"La tienda {req.StoreId} no existe." });

        chat.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        chat.StoreId = req.StoreId;
        chat.IsActive = req.IsActive;
        await _db.SaveChangesAsync(ct);
        return Ok(new { chat.ChatId, chat.Description, chat.IsActive, chat.StoreId, chat.CreatedAtUtc });
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

    /// <summary>Prueba a todos los chats activos o, con <paramref name="chatId"/>, solo a ese (activo o no).</summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTest([FromQuery] long? chatId, CancellationToken ct)
    {
        var allChats = await _db.TelegramChats.AsNoTracking().ToListAsync(ct);
        var activeChats = chatId is null
            ? allChats.Where(c => c.IsActive).ToList()
            : allChats.Where(c => c.ChatId == chatId).ToList();
        if (chatId is not null && activeChats.Count == 0)
            return NotFound(new { error = $"Chat {chatId} no encontrado." });
        if (activeChats.Count == 0)
            return BadRequest(new { error = "No hay chats activos registrados para enviar el mensaje de prueba." });

        var token = await TelegramToken.ResolverAsync(_db, _telegramOptions.Value, ct);
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

    /// <summary>
    /// Vista previa de los avisos, con el mismo texto que manda el Worker (TelegramMensajes): una
    /// tienda de ejemplo crítica y su recuperación, a la hora de negocio.
    /// </summary>
    [HttpGet("preview")]
    public IActionResult Preview([FromServices] IConfiguration configuration)
    {
        var zona = Application.Services.BusinessTimeZoneClock.Resolve(configuration["Dashboard:BusinessTimeZone"]);
        var hora = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zona).ToString("dd/MM HH:mm");
        return Ok(new
        {
            alerta = Application.Services.TelegramMensajes.Alerta(
                "SAN VICENTE", 12, "critical", "Cola actual mayor o igual a 20 trabajos", 23, 1, hora),
            recuperada = Application.Services.TelegramMensajes.Recuperada("SAN VICENTE", 12, "critical", hora)
        });
    }

    // --- DTOs ---
    public record UpdateChatRequest(string? Description, int? StoreId, bool IsActive);

    public record PutConfigRequest(string? MinSeverity, bool NotifyOnRecovery, int CheckIntervalMinutes, bool? SoloEnHorario = null);
    public record PutTokenRequest(string? BotToken);
    public record AddChatRequest(long ChatId, string? Description, int? StoreId);
}
