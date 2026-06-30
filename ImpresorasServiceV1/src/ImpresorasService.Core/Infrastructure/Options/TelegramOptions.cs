namespace ImpresorasService.Infrastructure.Options;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public bool Enabled { get; set; } = false;
    public string BotToken { get; set; } = string.Empty;
}
