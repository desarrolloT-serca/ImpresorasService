namespace ImpresorasService.Domain.Entities;

public class RoutingRule
{
    public int RuleId { get; set; }
    public int Priority { get; set; }
    public int? StoreId { get; set; }
    public string? DocumentType { get; set; }
    public string? Channel { get; set; }
    public int PrinterId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset? ValidToUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Printer Printer { get; set; } = null!;
}
