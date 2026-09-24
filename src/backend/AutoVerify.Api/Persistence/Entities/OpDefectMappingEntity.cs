namespace AutoVerify.Api.Persistence.Entities;

/// <summary>
/// Dictionary mapping table: Maps Operator (OP) defect names/codes to Machine AOI defect names and defect families.
/// Distinguishes whether the OP confirmed a true defect or judged it as acceptable/noise (Overkill).
/// </summary>
public class OpDefectMappingEntity
{
    public Guid Id { get; set; }
    public string MachineCode { get; set; } = "ALL";
    public string PackageName { get; set; } = "Default";
    public string OpDefectName { get; set; } = string.Empty;
    public string MachineDefectName { get; set; } = string.Empty;
    public string DefectFamily { get; set; } = "Optical";
    public string ComponentGroup { get; set; } = string.Empty;
    
    /// <summary>
    /// True if OP confirms this is a genuine defect.
    /// False if OP determines this is clean, false alarm, dust, or acceptable noise (Overkill).
    /// </summary>
    public bool IsConfirmedDefect { get; set; } = true;

    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
