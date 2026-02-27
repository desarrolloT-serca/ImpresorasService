namespace ImpresorasService.Domain.Entities;

public class Printer
{
    public int PrinterId { get; set; }
    public string PrinterName { get; set; } = string.Empty;
    public string SpoolQueue { get; set; } = string.Empty;
    public int StoreId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CapabilitiesJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
