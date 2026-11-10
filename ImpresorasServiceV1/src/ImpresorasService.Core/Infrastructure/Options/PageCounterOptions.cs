namespace ImpresorasService.Infrastructure.Options;

/// <summary>
/// Lectura diaria del contador de páginas (ERP_SERCA docs/73, opción E): a las 00:00 de la zona de negocio
/// (<c>Dashboard:BusinessTimeZone</c>) se lee el total de cada impresora activa con IP.
/// </summary>
public sealed class PageCounterOptions
{
    public const string SectionName = "PageCounter";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minutos entre intentos con las impresoras que no respondieron (cada una ya se reintenta una vez al
    /// momento), hasta que respondan o empiece otro día.
    /// </summary>
    public int RetryMinutes { get; set; } = 5;
}
