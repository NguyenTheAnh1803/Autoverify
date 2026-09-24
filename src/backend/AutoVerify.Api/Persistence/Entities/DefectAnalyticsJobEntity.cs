namespace AutoVerify.Api.Persistence.Entities;

public sealed class DefectAnalyticsJobEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public DefectPreviewSessionEntity Session { get; set; } = null!;
    public string AnalysisType { get; set; } = string.Empty; // SPC, PCA, MSA, EWMA, CUSUM, DOE
    public string Status { get; set; } = "Queued"; // Queued, Computing, Completed, Failed, Cancelled, Expired
    public int ProgressPercent { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string ConfigurationJson { get; set; } = "{}";
    public string? ResultJson { get; set; }
    public string? WarningsJson { get; set; }
    public string? ErrorMessage { get; set; }
    public int TtlMinutes { get; set; } = 120;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string CreatedBy { get; set; } = "system";
}
