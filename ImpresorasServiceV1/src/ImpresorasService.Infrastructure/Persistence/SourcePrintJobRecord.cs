namespace ImpresorasService.Infrastructure.Persistence;

public class SourcePrintJobRecord
{
    public long Id { get; set; }
    public string SourceSystem { get; set; } = string.Empty;
    public string ExternalJobId { get; set; } = string.Empty;
    public int StoreId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string? Channel { get; set; }
    public byte[] PdfBlob { get; set; } = Array.Empty<byte>();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsProcessed { get; set; }
}
