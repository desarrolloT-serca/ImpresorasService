namespace ImpresorasService.Application.Models;

public sealed record IncomingPrintJob(
    long SourceJobId,
    string SourceSystem,
    string ExternalJobId,
    int StoreId,
    string DocumentType,
    string Channel,
    byte[] PdfBlob,
    DateTimeOffset CreatedAtUtc
);
