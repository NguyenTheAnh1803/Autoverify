using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

public sealed class SeaweedFsOptions
{
    public string? BaseUrl { get; set; }
    public string LocalFallbackPath { get; set; } = "data/storage";
    public string DefaultBucket { get; set; } = "autoverify";
}

public class SeaweedFsStorageManager(HttpClient httpClient, IOptions<SeaweedFsOptions> options, IWebHostEnvironment environment)
{
    private readonly SeaweedFsOptions _options = options.Value;

    public string Mode => string.IsNullOrWhiteSpace(_options.BaseUrl) ? "LocalFallback" : "SeaweedFS";

    public async Task<StoredObjectResponse> UploadJsonAsync(string? bucket, string? objectName, object payload, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions { WriteIndented = true });
        return await UploadBytesAsync(bucket, objectName, bytes, "application/json", cancellationToken);
    }

    public async Task<StoredObjectResponse> UploadImageAsync(string? bucket, string objectName, Stream imageStream, string? contentType, CancellationToken cancellationToken = default)
    {
        using var memory = new MemoryStream();
        await imageStream.CopyToAsync(memory, cancellationToken);
        return await UploadBytesAsync(bucket, objectName, memory.ToArray(), string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, cancellationToken);
    }

    protected virtual async Task<StoredObjectResponse> UploadBytesAsync(string? bucket, string? objectName, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        var normalizedBucket = NormalizePathSegment(bucket ?? _options.DefaultBucket);
        var normalizedName = NormalizeObjectName(string.IsNullOrWhiteSpace(objectName) ? $"{Guid.NewGuid():N}" : objectName);
        var createdAt = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return await SaveLocalFallbackAsync(normalizedBucket, normalizedName, bytes, contentType, createdAt, cancellationToken);
        }

        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var uploadUrl = $"{baseUrl}/{Uri.EscapeDataString(normalizedBucket)}/{Uri.EscapeDataString(normalizedName)}";
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await httpClient.PutAsync(uploadUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return new StoredObjectResponse(Guid.NewGuid(), "SeaweedFS", normalizedBucket, normalizedName, contentType, bytes.LongLength, uploadUrl, createdAt);
    }

    private async Task<StoredObjectResponse> SaveLocalFallbackAsync(string bucket, string objectName, byte[] bytes, string contentType, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        var root = Path.IsPathRooted(_options.LocalFallbackPath)
            ? _options.LocalFallbackPath
            : Path.Combine(environment.ContentRootPath, _options.LocalFallbackPath);
        var folder = Path.Combine(root, bucket);
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, objectName);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return new StoredObjectResponse(Guid.NewGuid(), "LocalFallback", bucket, objectName, contentType, bytes.LongLength, path, createdAt);
    }

    private static string NormalizePathSegment(string value)
    {
        var sanitized = new string(value.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "autoverify" : sanitized;
    }

    private static string NormalizeObjectName(string value)
    {
        var safeName = Path.GetFileName(value.Trim());
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = $"{Guid.NewGuid():N}.bin";
        }

        var extension = Path.GetExtension(safeName);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(safeName);
        var sanitizedName = new string(nameWithoutExtension.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
        return string.IsNullOrWhiteSpace(sanitizedName)
            ? $"{Guid.NewGuid():N}{extension}"
            : $"{sanitizedName}{extension}";
    }
}

public sealed class NullSeedStorageManager() : SeaweedFsStorageManager(new HttpClient(), Options.Create(new SeaweedFsOptions()), new NullWebHostEnvironment())
{
    protected override Task<StoredObjectResponse> UploadBytesAsync(string? bucket, string? objectName, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        var normalizedBucket = bucket ?? "seed";
        var normalizedObjectName = objectName ?? $"{Guid.NewGuid():N}.json";
        return Task.FromResult(new StoredObjectResponse(Guid.NewGuid(), "Seed", normalizedBucket, normalizedObjectName, contentType, bytes.LongLength, $"seed://{normalizedBucket}/{normalizedObjectName}", DateTimeOffset.UtcNow));
    }
}

internal sealed class NullWebHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "AutoVerify.Api";
    public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
