namespace AutoVerify.Api.Persistence.Entities;

public class ScanListEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MappingName { get; set; } = string.Empty;
    public string MachineCode { get; set; } = "UNASSIGNED";
    public string LineCode { get; set; } = "ALL";
    public string VerificationCategory { get; set; } = "Leakage";
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int DuplicateCount { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string? EvidenceUri { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}
