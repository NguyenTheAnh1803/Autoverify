namespace AutoVerify.Api.Persistence.Entities;

public class ProductRecordEntity
{
    public Guid Id { get; set; }
    public string KeyId { get; set; } = string.Empty;
    public string Judge { get; set; } = "OK";
    public string PanelId { get; set; } = string.Empty;
    public string PanelJudge { get; set; } = "OK";
    public string NameDefect { get; set; } = "None";
    public string? Zone { get; set; }
    public string? DefectName { get; set; }
    public string MachineCode { get; set; } = "MACHINE-A";
    public string LineCode { get; set; } = "LINE-1";
    public string ModelCode { get; set; } = "UNASSIGNED";
    public string? OpReviewMark { get; set; }
    public string? OpReviewJudge { get; set; }
    public string? OpReviewDefectName { get; set; }
    public string? OpReviewMappedDefect { get; set; }
    public string? OpReviewNote { get; set; }
    public DateTimeOffset? OpReviewedAtUtc { get; set; }
    public DateTimeOffset InspectedAtUtc { get; set; }
    public string? MetadataJson { get; set; }
    public string? DefectPackUrl { get; set; }
}
