namespace AutoVerify.Api.Services;

public sealed class ImageCrawlerOptions
{
    public const string SectionName = "ImageCrawler";

    public string DefaultApiUrl { get; set; } = "http://10.121.45.43:336/api/image/get";
    public string ImageRoot { get; set; } = @"\\192.168.200.121\d\defectimage";
    public int BatchMaxItems { get; set; } = 100;
    public bool AllowExternalUrls { get; set; }
}
