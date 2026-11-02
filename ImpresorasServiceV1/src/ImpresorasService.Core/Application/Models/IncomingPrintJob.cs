namespace ImpresorasService.Application.Models;

public sealed record IncomingPrintJob(
    long SourceJobId,
    string SourceSystem,
    string ExternalJobId,
    int StoreId,
    string DocumentType,
    string Channel,
    byte[] PdfBlob,
    DateTimeOffset CreatedAtUtc,
    // Motivo por el que pdf_url no sirve (y no servirá): el trabajo entra directo en ErrorFinal.
    string? PdfError = null
);
