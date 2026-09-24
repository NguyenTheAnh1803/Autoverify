namespace AutoVerify.Api.Persistence.Entities;

public class VerificationHistoryEntity
{
    public Guid Id { get; set; }
    public string Category { get; set; } = "Overkill";
    public string EntityId { get; set; } = string.Empty;
    public string LineCode { get; set; } = "ALL";
    public string? Mapping { get; set; }
    public string MachineCode { get; set; } = "UNASSIGNED";
    public string Judge { get; set; } = "Unknown";
    public string PanelJudge { get; set; } = "Unknown";
    public string NameDefect { get; set; } = "None";
    public string AnomalyStatus { get; set; } = "Pending";
    public double AnomalyScore { get; set; }
    public string AnalysisNotes { get; set; } = string.Empty;
    public int TotalDefects { get; set; }
    public bool Eligible { get; set; }
    public Guid? ScanListId { get; set; }
    public Guid? QueryProfileId { get; set; }
    public Guid? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
    public string? QueryName { get; set; }
    public string? PayloadJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
