namespace AutoVerify.Api.Persistence.Entities;

public class ScanListItemEntity
{
    public Guid Id { get; set; }
    public Guid ScanListId { get; set; }
    public int RowNumber { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string LineCode { get; set; } = "ALL";
    public int? MappingInt { get; set; }
    public string? Mapping { get; set; }
}
