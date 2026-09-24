namespace AutoVerify.Api.Persistence.Entities;

public class StorageObjectEntity
{
    public Guid Id { get; set; }
    public string Backend { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Uri { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
