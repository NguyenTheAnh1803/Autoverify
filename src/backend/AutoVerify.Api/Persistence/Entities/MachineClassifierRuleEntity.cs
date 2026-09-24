namespace AutoVerify.Api.Persistence.Entities;

public sealed class MachineClassifierRuleEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RuleCode { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string? ProductKey { get; set; }
    public string? LineCode { get; set; }
    public string? MachineCode { get; set; }
    public string? StationCode { get; set; }
    public string? DefectFamily { get; set; }
    public string? DefectClass { get; set; }
    public string ThresholdsConfigJson { get; set; } = "{}";
    public string? ModelMetadataJson { get; set; }
    public string? ScriptContent { get; set; }
    public int Priority { get; set; } = 0;
    public string? DefectTypeId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset EffectiveFromUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EffectiveToUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = "system";
    public string? Description { get; set; }
}
