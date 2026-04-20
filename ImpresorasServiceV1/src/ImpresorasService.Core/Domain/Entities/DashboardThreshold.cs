namespace ImpresorasService.Domain.Entities;

public class DashboardThreshold
{
    public int Id { get; set; }
    public int WarningQueueMin { get; set; } = 10;
    public int CriticalQueueMin { get; set; } = 30;
    public string QueueWarningSeverity { get; set; } = "warning";
    public string QueueCriticalSeverity { get; set; } = "critical";
    public int WarningFailedWithoutRetryMin { get; set; } = 1;
    public int CriticalFailedWithoutRetryMin { get; set; } = 5;
    public string FailedWarningSeverity { get; set; } = "warning";
    public string FailedCriticalSeverity { get; set; } = "critical";
    public int MissingHostMin { get; set; } = 1;
    public string MissingHostSeverity { get; set; } = "warning";
    public int ConnWarningFailuresMin { get; set; } = 2;
    public int ConnCriticalFailuresMin { get; set; } = 3;
    public string ConnWarningSeverity { get; set; } = "warning";
    public string ConnCriticalSeverity { get; set; } = "critical";
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
