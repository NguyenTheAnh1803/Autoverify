namespace AutoVerify.Api.Persistence.Entities;

public class DefectRecordEntity
{
    public Guid Id { get; set; }
    public string KeyId { get; set; } = string.Empty;
    public string DefectTable { get; set; } = "AOI_DEFECTS";
    public string DefectName { get; set; } = string.Empty;
    public string DefectType { get; set; } = "Optical";
    public string? ComponentName { get; set; }
    public double? CoordinateX { get; set; }
    public double? CoordinateY { get; set; }
    public string Severity { get; set; } = "Minor";
    public string Judge { get; set; } = "NG";
    public double ConfidenceScore { get; set; } = 1.0;
    public string? ImageUri { get; set; }
    public string? ImageUrl { get; set; }
    public string? Feature1StorageUri { get; set; }
    public string? Feature2StorageUri { get; set; }
    public string? FeatureUrl { get; set; }
    public string? CustomFeatureUrl { get; set; }
    public string? FeatureJson { get; set; }
    public string? CustomFeatureJson { get; set; }
    public string? Reason { get; set; }
    public string? EqpName { get; set; }
    public string? EmNo { get; set; }
    public string? SpecName { get; set; }
    public string? SpecJudge { get; set; }
    public string? SpecReason { get; set; }
    public int? Priority { get; set; }
    public string ModelCode { get; set; } = "UNASSIGNED";
    public Guid? ConnectionId { get; set; }
    public string MachineCode { get; set; } = "MACHINE-A";
    public string LineCode { get; set; } = "LINE-1";
    public DateTimeOffset CreatedAtUtc { get; set; }
}
