namespace ImpresorasService.Application.Abstractions;

public interface ITelegramNotifier
{
    Task SendAlertAsync(string message, CancellationToken ct, int? storeId = null);
}
