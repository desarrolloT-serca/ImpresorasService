namespace ImpresorasService.Domain.Entities;

public class TelegramChat
{
    /// <summary>Chat ID de Telegram (positivo = usuario/grupo, negativo = supergrupo/canal).</summary>
    public long ChatId { get; set; }

    /// <summary>Etiqueta descriptiva para identificar el chat en la interfaz.</summary>
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Null = recibe alertas de todas las tiendas. Con valor = solo recibe alertas de esa tienda.</summary>
    public int? StoreId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
