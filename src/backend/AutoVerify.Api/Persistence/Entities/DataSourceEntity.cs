namespace AutoVerify.Api.Persistence.Entities;

public class DataSourceEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string MachineCode { get; set; } = "UNASSIGNED";
    public string VerificationCategory { get; set; } = "Overkill";
    public string SchedulePeriod { get; set; } = "Weekly";
    public string Environment { get; set; } = string.Empty;
    public string Health { get; set; } = string.Empty;
    public int AverageLatencyMs { get; set; }
    public bool Enabled { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? DatabaseName { get; set; }
    public string? Username { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? EncryptedSecret { get; set; }
}
