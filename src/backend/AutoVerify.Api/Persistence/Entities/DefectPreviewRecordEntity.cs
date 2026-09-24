namespace AutoVerify.Api.Persistence.Entities;

public sealed class DefectPreviewRecordEntity
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public DefectPreviewSessionEntity Session { get; set; } = null!;

    public Guid ObservationId { get; set; } = Guid.NewGuid();
    public int RowIndex { get; set; }
    public string? DefectId { get; set; }
    public string? KeyId { get; set; }
    public string DefectName { get; set; } = string.Empty;
    public string DefectTable { get; set; } = "AOI_DEFECTS";
    public string DefectType { get; set; } = "Optical";
    public string? ComponentName { get; set; }
    public double? CoordinateX { get; set; }
    public double? CoordinateY { get; set; }
    public string Severity { get; set; } = "Minor";
    public string Judge { get; set; } = "NG";
    public double ConfidenceScore { get; set; } = 1.0;
    public string? Reason { get; set; }
    public string? EqpName { get; set; }
    public string? EmNo { get; set; }
    public string? SpecName { get; set; }
    public string? SpecJudge { get; set; }
    public string? SpecReason { get; set; }
    public int? Priority { get; set; }

    public string? LineCode { get; set; }
    public string? MachineCode { get; set; }
    public string? StationCode { get; set; }
    public string? ClassifierRuleCode { get; set; }
    public string? ClassifierRuleName { get; set; }
    public string? ClassifierRuleVersion { get; set; }
    public string? DefectFamily { get; set; }
    public string? DefectClass { get; set; }

    public bool HasFeatureJson { get; set; }
    public string? FeatureJsonSnippet { get; set; }
    public string? RawFeatureUri { get; set; }
    public string? RawImageUri { get; set; }
    public DateTimeOffset? InspectedAtUtc { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<DefectPreviewFeatureEntity> ExtractedFeatures { get; set; } = [];
}
