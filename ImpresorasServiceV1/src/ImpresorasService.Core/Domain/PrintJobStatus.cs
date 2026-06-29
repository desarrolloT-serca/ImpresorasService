namespace ImpresorasService.Domain;

public enum PrintJobStatus
{
    Pending = 0,
    Routed = 1,
    Printing = 2,
    SpoolAccepted = 3,
    PrintedConfirmed = 4,
    PrintedUnknown = 5,
    RetryScheduled = 6,
    Cancelled = 7,
    ErrorFinal = 8,
    PrinterBlocked = 9
}
