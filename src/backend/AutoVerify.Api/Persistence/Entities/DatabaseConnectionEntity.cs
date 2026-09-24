namespace AutoVerify.Api.Persistence.Entities;

public class DatabaseConnectionEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Engine { get; set; } = "MariaDB";
    public string MachineCode { get; set; } = "UNASSIGNED";
    public string LineCode { get; set; } = "UNASSIGNED";
    public string ModelCode { get; set; } = "UNASSIGNED";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? ApiBaseUrl { get; set; }
    public string? ImageCrawlerApiUrl { get; set; }
    public string ImageCrawlerVpMode { get; set; } = "0";
    public string EncryptedPassword { get; set; } = string.Empty;
    public string Health { get; set; } = "Unknown";
    public int AverageLatencyMs { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
