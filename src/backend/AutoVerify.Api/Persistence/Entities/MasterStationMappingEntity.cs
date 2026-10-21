namespace AutoVerify.Api.Persistence.Entities;

/// <summary>Maps an AutoVerify machine/line/model to the Master Server station and product that hold its AI models.</summary>
public class MasterStationMappingEntity
{
    public Guid Id { get; set; }
    public string MachineCode { get; set; } = string.Empty;
    public string LineCode { get; set; } = string.Empty;
    public string ModelCode { get; set; } = "UNASSIGNED";
    public string StationId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>When true (and AiVerification:AutoSubmit is on), new products of this machine/line/model are sent to the Master automatically.</summary>
    public bool AutoSubmit { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
