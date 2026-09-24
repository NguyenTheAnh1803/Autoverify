using AutoVerify.Api.Persistence.Entities;

namespace AutoVerify.Api.Storage;

public sealed record DefectBundleEntryInfo(
    string? FeatureEntryName,
    string? CustomFeatureEntryName,
    string? ImageEntryName,
    string? FeatureUrl,
    string? CustomFeatureUrl,
    string? ImageUrl
);

public sealed record ProductBundleUploadResult(
    string BundleUri,
    string ShardedPath,
    long OriginalBytes,
    long CompressedBytes,
    double CompressionRatio,
    int FilesCount,
    IReadOnlyDictionary<Guid, DefectBundleEntryInfo> DefectEntries
);

public interface IStorageService
{
    string Mode { get; }
    Task<StoredObjectResponse> UploadJsonAsync(string? bucket, string? objectName, object payload, CancellationToken cancellationToken = default);
    Task<StoredObjectResponse> UploadJsonStringAsync(string? bucket, string? objectName, string jsonContent, CancellationToken cancellationToken = default);
    Task<StoredObjectResponse> UploadJsonGzipAsync(string? bucket, string? objectName, string jsonContent, CancellationToken cancellationToken = default);
    Task<StoredObjectResponse> UploadImageAsync(string? bucket, string objectName, Stream imageStream, string? contentType, CancellationToken cancellationToken = default);
    Task<ProductBundleUploadResult> UploadProductDefectBundleAsync(string keyId, IEnumerable<DefectRecordEntity> defects, IDictionary<string, byte[]>? imageFiles = null, CancellationToken cancellationToken = default);
    Task<string> UploadProductDefectPackAsync(string keyId, IEnumerable<DefectRecordEntity> defects, CancellationToken cancellationToken = default);
    Task<string?> ReadDefectPackJsonAsync(string packLocationOrUrl, CancellationToken cancellationToken = default);
    Task<(byte[] Data, string ContentType)?> ExtractZipEntryAsync(string zipLocationOrUrl, string entryNameOrPrefix, CancellationToken cancellationToken = default);
    Task<string?> ReadTextAsync(string locationOrUrl, CancellationToken cancellationToken = default);
    Task<StorageStreamResult?> OpenReadStreamAsync(string locationOrUrl, CancellationToken cancellationToken = default);
}
