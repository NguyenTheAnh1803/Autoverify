namespace AutoVerify.Api.Persistence.Entities;

public sealed class DefectPreviewSessionEntity
{
    public Guid Id { get; set; }
    public string StorageUri { get; set; } = string.Empty;
    public string SourceUri { get => StorageUri; set => StorageUri = value; }
    public string? KeyId { get; set; }
    public string Format { get; set; } = "DefectPackJson";
    public string SourceType { get => Format; set => Format = value; }
    public string? DetectedFormat { get; set; }
    public string? ProcessingStage { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Ingesting, Ready, Failed, Expired
    public int TotalRowsCount { get; set; }
    public int TotalRecords { get => TotalRowsCount; set => TotalRowsCount = value; }
    public int LoadedRowsCount { get; set; }
    public int ProcessedRecords { get; set; }
    public int NgCount { get; set; }
    public int OkCount { get; set; }
    public string? SeverityBreakdownJson { get; set; }
    public long SourceSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public string? WarningMessage { get; set; }
    public int RepairsCount { get; set; }
    public string? FilterSummaryJson { get; set; }
    public int TtlMinutes { get; set; } = 60;
    public int MaxRows { get; set; } = 50000;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? LastAccessedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string CreatedBy { get; set; } = "system";

    public ICollection<DefectPreviewRecordEntity> Records { get; set; } = new List<DefectPreviewRecordEntity>();
    public ICollection<DefectPreviewFeatureEntity> Features { get; set; } = new List<DefectPreviewFeatureEntity>();
    public ICollection<DefectAnalyticsJobEntity> AnalyticsJobs { get; set; } = new List<DefectAnalyticsJobEntity>();
}
