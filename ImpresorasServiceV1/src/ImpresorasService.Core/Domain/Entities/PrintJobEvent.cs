using ImpresorasService.Domain;

namespace ImpresorasService.Domain.Entities;

public class PrintJobEvent
{
    public long EventId { get; set; }
    public Guid JobId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public PrintJobStatus? OldStatus { get; set; }
    public PrintJobStatus? NewStatus { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public string ActorType { get; set; } = "system";
    public string? ActorId { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}
