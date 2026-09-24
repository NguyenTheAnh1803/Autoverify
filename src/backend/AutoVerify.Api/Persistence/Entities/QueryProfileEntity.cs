namespace AutoVerify.Api.Persistence.Entities;

public class QueryProfileEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? DataSourceId { get; set; }
    public string MachineCode { get; set; } = string.Empty;
    public string LineCode { get; set; } = string.Empty;
    public string VerificationCategory { get; set; } = "Overkill";
    public string Concept { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string SqlTemplate { get; set; } = string.Empty;
    public string ParameterSchemaJson { get; set; } = "{}";
    public int TimeoutSeconds { get; set; }
    public int MaxConcurrency { get; set; }
    public bool Enabled { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
