namespace AutoVerify.Api.Persistence.Entities;

/// <summary>The Master AI's judgement of one defect image. One row per defect; Status says whether a real verdict exists.</summary>
public class AiVerdictEntity
{
    public Guid Id { get; set; }
    public Guid DefectId { get; set; }
    public string KeyId { get; set; } = string.Empty;
    public string MachineCode { get; set; } = string.Empty;
    public string LineCode { get; set; } = string.Empty;
    public string ModelCode { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;

    /// <summary>"Submitted" while the Master works on it, then "Done", "Rejected" or "Unavailable". Only "Done" carries a verdict.</summary>
    public string Status { get; set; } = AiVerdictStatus.Submitted;
    public string? MasterJobId { get; set; }
    public string? Verdict { get; set; }
    public double? AnomalyScore { get; set; }
    public string? DefectClass { get; set; }
    public string? BoxesJson { get; set; }
    public string? ModelVersionsJson { get; set; }
    public string? ClientMode { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public static class AiVerdictStatus
{
    public const string Submitted = "Submitted";
    public const string Done = "Done";
    public const string Rejected = "Rejected";
    public const string Unavailable = "Unavailable";
}
