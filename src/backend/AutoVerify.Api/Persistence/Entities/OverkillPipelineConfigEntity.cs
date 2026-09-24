namespace AutoVerify.Api.Persistence.Entities;

public class OverkillPipelineConfigEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid DatabaseConnectionId { get; set; }
    public string MachineCode { get; set; } = "MACHINE-A";
    public string LineCode { get; set; } = "LINE-1";
    public string ModelCode { get; set; } = "UNASSIGNED";
    public bool Enabled { get; set; } = true;

    // Scheduling
    public int ScheduleIntervalMinutes { get; set; } = 120;
    public DateTimeOffset? LastRunAtUtc { get; set; }
    public DateTimeOffset? NextRunAtUtc { get; set; }
    public string LastRunStatus { get; set; } = "Idle";
    public string? LastRunMessage { get; set; }
    public int LastRunProductsCount { get; set; }
    public int LastRunDefectsCount { get; set; }

    // Product Table Configuration
    public string ProductTableName { get; set; } = "Product";
    public string IdColumnName { get; set; } = "ID";
    public string FkIdColumnName { get; set; } = "Fk_ID";
    public string JudgeColumnName { get; set; } = "Judge";
    public string JudgeFilterValuesJson { get; set; } = "[\"N\",\"NG\"]";
    public bool IncludeOkProducts { get; set; } = true;
    public string? TimestampColumnName { get; set; } = "InspectedAt";
    public string? ZoneColumnName { get; set; } = "zone";
    public string? DefectNameColumnName { get; set; } = "defect_name";
    public string? DateTimeInspectionColumnName { get; set; } = "datetime_inspection";
    public string? InspectedAtColumnName
    {
        get => DateTimeInspectionColumnName;
        set => DateTimeInspectionColumnName = value;
    }
    public int LookbackHours { get; set; } = 6;
    public int BatchSize { get; set; } = 1000;

    // Multi-Defect Tables Configuration (Serialized JSON array of DefectTableMapping)
    public string DefectTablesJson { get; set; } = "[]";

    // SeaweedFS Defect Offloading
    public bool OffloadDefectsToSeaweedFs { get; set; } = true;

    // PC Image Crawler API Configuration (Individual per PC / pipeline)
    public string? ImageCrawlerApiUrl { get; set; }
    public bool AutoCrawlImages { get; set; } = false;
    public string ImageCrawlerVpMode { get; set; } = "0";

    // Defect Query Bypass Rule (Serialized JSON of DefectBypassRuleDto)
    public string? DefectBypassRuleJson { get; set; }
    public int LastRunDefectsBypassedCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
