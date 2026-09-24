namespace AutoVerify.Api.Persistence.Entities;

public class OverkillPipelineExecutionEntity
{
    public Guid Id { get; set; }
    public Guid ConfigId { get; set; }
    public string PipelineName { get; set; } = string.Empty;
    public string TriggerType { get; set; } = "Scheduled";
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string Status { get; set; } = "Running";
    public int ProductsScanned { get; set; }
    public int DefectsExtracted { get; set; }
    public int OverkillCandidatesCount { get; set; }
    public int DefectsBypassed { get; set; }
    public int DefectsScanned { get; set; }
    public string? BypassRuleSummary { get; set; }
    public int LatencyMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ExecutionSummaryJson { get; set; }
}
