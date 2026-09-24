namespace AutoVerify.Api.Persistence.Entities;

/// <summary>
/// Represents inspection records uploaded from an Operator (OP) Excel or CSV sheet.
/// Used to reconcile against Machine AOI judgments to separate Overkill from Real Defects.
/// </summary>
public class OpInspectionRecordEntity
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid? ProductRecordId { get; set; }
    public ProductRecordEntity? ProductRecord { get; set; }
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// OP human judgment: "OK", "NG", "PASS", "FAIL", "G", "N"
    /// </summary>
    public string OpJudge { get; set; } = "OK";

    /// <summary>
    /// Raw defect name entered by OP (e.g. "Solder bridge", "Corner crack", "No defect")
    /// </summary>
    public string? OpDefectName { get; set; }

    /// <summary>
    /// Machine defect name mapped via OpDefectMapping dictionary
    /// </summary>
    public string? MappedMachineDefect { get; set; }

    /// <summary>
    /// Defect family mapped via OpDefectMapping dictionary
    /// </summary>
    public string? MappedDefectFamily { get; set; }

    /// <summary>
    /// True if OP confirms a true defect, false if OP confirms OK/False Alarm
    /// </summary>
    public bool? IsRealDefectConfirmed { get; set; }

    public string? LineCode { get; set; }
    public string? MachineCode { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset InspectedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
