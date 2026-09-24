namespace AutoVerify.Api.Persistence.Entities;

public static class ProductQueryStatuses
{
    public const string Pending = "Pending";
    public const string Doing = "Doing";
    public const string Judge = "Judge";

    public static bool IsValid(string status) => status is Pending or Doing or Judge;
}

public class ProductQueryEntity
{
    public Guid Id { get; set; }
    public string QueryKey { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string Status { get; set; } = ProductQueryStatuses.Pending;
    public string PayloadJson { get; set; } = string.Empty;
    public string? JudgeResult { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
