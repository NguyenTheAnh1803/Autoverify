namespace AutoVerify.Api.Services;

public sealed class DefectPreviewOptions
{
    public const string SectionName = "DefectPreview";

    public int MaxConcurrentIngestions { get; set; } = 2;
    public int DefaultTtlMinutes { get; set; } = 120;
    public int BatchInsertSize { get; set; } = 1000;
    public int CleanupIntervalMinutes { get; set; } = 10;
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 200;
    public int FeatureSnippetMaxLength { get; set; } = 500;
    public int MaxRowsLimit { get; set; } = 50000;
    public bool EnableSafeJsonRecovery { get; set; } = true;
}
