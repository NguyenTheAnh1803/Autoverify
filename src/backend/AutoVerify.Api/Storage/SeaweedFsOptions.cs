namespace AutoVerify.Api.Storage;

public sealed class SeaweedFsOptions
{
    public string? MasterUrl { get; set; } = "http://localhost:9333";
    public string? FilerUrl { get; set; } = "http://localhost:8888";
    
    private string? _baseUrl;
    public string? BaseUrl
    {
        get => !string.IsNullOrWhiteSpace(FilerUrl) ? FilerUrl : _baseUrl;
        set
        {
            _baseUrl = value;
            if (string.IsNullOrWhiteSpace(FilerUrl))
            {
                FilerUrl = value;
            }
        }
    }

    public string? PublicBaseUrl { get; set; } = "http://localhost:8888";
    public bool OffloadDefects { get; set; } = true;
    public string LocalFallbackPath { get; set; } = "data/storage";
    public string DefaultBucket { get; set; } = "autoverify";
}
