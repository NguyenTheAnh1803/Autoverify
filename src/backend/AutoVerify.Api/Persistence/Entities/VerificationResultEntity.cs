namespace AutoVerify.Api.Persistence.Entities;

public class VerificationResultEntity
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string RuleCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double AnomalyScore { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}
