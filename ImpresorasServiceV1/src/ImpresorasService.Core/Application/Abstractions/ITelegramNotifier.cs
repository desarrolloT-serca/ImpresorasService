namespace ImpresorasService.Application.Abstractions;

public interface ITelegramNotifier
{
    /// <summary>
    /// Devuelve <c>true</c> solo si el mensaje fue aceptado por AL MENOS un chat. Antes devolvia
    /// void y se tragaba los fallos por chat, asi que el caller registraba "alerta enviada" sin
    /// tener ninguna prueba de que nadie la hubiera recibido.
    /// </summary>
    Task<bool> SendAlertAsync(string message, CancellationToken ct, int? storeId = null);
}
