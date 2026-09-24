namespace AutoVerify.Api.Persistence.Entities;

public class LeakageBatchEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // "Pending", "InProgress", "Completed", "PartiallyFailed"
    public string SourceType { get; set; } = "Paste"; // "Paste", "Csv"
    public string? RawInputSummary { get; set; }
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int ProcessingCount { get; set; }
    public int CompletedCount { get; set; }
    public int NotFoundCount { get; set; }
    public int LeakageCount { get; set; }
    public int ConsistentCount { get; set; }
    public int FailedCount { get; set; }
    public string CreatedBy { get; set; } = "admin";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public ICollection<LeakageQueueItemEntity> Items { get; set; } = new List<LeakageQueueItemEntity>();
}
