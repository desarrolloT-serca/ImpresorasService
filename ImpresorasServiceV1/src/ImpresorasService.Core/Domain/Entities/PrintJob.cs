using ImpresorasService.Domain;

namespace ImpresorasService.Domain.Entities;

public class PrintJob
{
    public Guid JobId { get; set; }
    public string SourceSystem { get; set; } = string.Empty;
    public string ExternalJobId { get; set; } = string.Empty;
    public int StoreId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string Channel { get; set; } = "DEFAULT";
    public byte[]? PdfBlob { get; set; }
    public string PdfSha256 { get; set; } = string.Empty;
    public PrintJobStatus Status { get; set; } = PrintJobStatus.Pending;
    public int? PrinterId { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextRetryAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
