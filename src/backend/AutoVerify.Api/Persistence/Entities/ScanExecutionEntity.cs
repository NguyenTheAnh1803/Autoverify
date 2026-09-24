namespace AutoVerify.Api.Persistence.Entities;

public class ScanExecutionEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ScanListId { get; set; }
    public string ScanListName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SourceNamesJson { get; set; } = "[]";
    public int TotalItems { get; set; }
    public int PassedItems { get; set; }
    public int WarningItems { get; set; }
    public int FailedItems { get; set; }
    public int TechnicalErrorItems { get; set; }
    public int AnomalyItems { get; set; }
    public string? EvidenceUri { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}
