namespace ImpresorasService.Application.Models;

public sealed record IncomingPrintJob(
    string SourceSystem,
    string ExternalJobId,
    int StoreId,
    string DocumentType,
    string Channel,
    byte[] PdfBlob,
    DateTimeOffset CreatedAtUtc
);
