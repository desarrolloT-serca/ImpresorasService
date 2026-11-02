using System.Net;

namespace ImpresorasService.Application.Services;

/// <summary>
/// Texto de los avisos de Telegram (HTML de Telegram: b, i, code). El mismo para el Worker, que los
/// manda, y para la vista previa de la pantalla de Telegram: lo que se ve es lo que llega.
/// </summary>
public static class TelegramMensajes
{
    private const string Linea = "—————————————————";

    public static string Alerta(string tienda, int storeId, string health, string motivo, int cola, int fallos, string hora)
    {
        var icono = health == "critical" ? "🔴" : "🟡";
        var etiqueta = health == "critical" ? "CRÍTICA" : "WARNING";
        return $"{Linea}\n\n" +
            $"{icono} <b>{etiqueta}</b> · <b>{Html(tienda)}</b> <code>#{storeId}</code>\n" +
            $"{Html(motivo)}\n\n" +
            $"📦 Cola <b>{cola}</b> · ❌ Fallos <b>{fallos}</b>\n" +
            $"🕒 {hora}\n\n" +
            $"{Linea}";
    }

    public static string Recuperada(string tienda, int storeId, string saludAnterior, string hora) =>
        $"{Linea}\n\n" +
        $"🟢 <b>RECUPERADA</b> · <b>{Html(tienda)}</b> <code>#{storeId}</code>\n" +
        $"<code>{Html(saludAnterior)}</code> → <b>saludable</b>\n\n" +
        $"🕒 {hora}\n\n" +
        $"{Linea}";

    /// <summary>
    /// Historial de avisos (27/10/2026) sin tabla propia: un evento de este tipo en
    /// printer_print_job_event por aviso, con el resumen en message y {storeId, health, delivered} en
    /// metadata_json. Si algún día molesta ahí, iría a una tabla suya (una).
    /// </summary>
    public const string EventoHistorial = "TELEGRAM_ALERT";

    /// <summary>
    /// job_id fijo por tienda para esos eventos: no es ningún trabajo (las consultas de trabajos cruzan
    /// con printer_print_job y no los ven) y el historial se lee por el índice (job_id, occurred_at_utc).
    /// </summary>
    public static Guid JobIdHistorial(int storeId) => new($"7e1e9a00-0000-4000-8000-{storeId:D12}");

    // Un «&» o «<» en el nombre de una tienda rompía el HTML y Telegram rechazaba el mensaje.
    private static string Html(string texto) => WebUtility.HtmlEncode(texto);
}
