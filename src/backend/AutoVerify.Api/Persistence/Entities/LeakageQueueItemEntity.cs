namespace AutoVerify.Api.Persistence.Entities;

public class LeakageQueueItemEntity
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public string ProductId { get; set; } = string.Empty;
    public string? LineCode { get; set; }
    public string Status { get; set; } = "Pending"; // "Pending", "Processing", "Completed", "NotFound", "Failed"
    public string? ResolvedFkId { get; set; }
    public string? StationJudge { get; set; }
    public double AnomalyScore { get; set; }
    public bool IsLeakageSuspect { get; set; }
    public int DefectCount { get; set; }
    public Guid? ConnectionId { get; set; }
    public string? TableScanned { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public LeakageBatchEntity? Batch { get; set; }
}
