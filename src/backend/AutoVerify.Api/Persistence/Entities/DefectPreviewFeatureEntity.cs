namespace AutoVerify.Api.Persistence.Entities;

public sealed class DefectPreviewFeatureEntity
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public DefectPreviewSessionEntity Session { get; set; } = null!;
    public Guid ObservationId { get; set; }
    public int RowIndex { get; set; }
    public string FeatureName { get; set; } = string.Empty;
    public double FeatureValue { get; set; }
}
