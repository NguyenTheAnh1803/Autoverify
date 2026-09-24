using System.Security.Cryptography;
using System.Text;

namespace AutoVerify.Api.Storage;

/// <summary>
/// Utility for hierarchical path sharding to prevent directory inode exhaustion and filesystem degradation.
/// Implements 4-tier 2-hex chunking (e.g. /20/30/40/50/{fileName}) based on MD5 hash of the partition key.
/// Max 256 entries per level (00 to ff), ensuring even distribution across SeaweedFS Filer or local disks.
/// </summary>
public static class StoragePathSharder
{
    /// <summary>
    /// Computes the 4-tier sharded directory prefix from the specified partition key.
    /// Example: "PROD-998822" -> "20/3a/bc/4f"
    /// </summary>
    public static string GetShardedDirectory(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "00/00/00/00";
        }

        var normalizedKey = key.Trim();
        var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(normalizedKey));

        // Take first 4 bytes as 4 hex pairs (e.g. 20/3a/bc/4f)
        return $"{hashBytes[0]:x2}/{hashBytes[1]:x2}/{hashBytes[2]:x2}/{hashBytes[3]:x2}";
    }

    /// <summary>
    /// Gets a sharded relative path for a file under a specific key.
    /// Example: key "PROD-1", fileName "report.json" -> "20/3a/bc/4f/report.json"
    /// </summary>
    public static string GetShardedPath(string key, string fileName)
    {
        var dir = GetShardedDirectory(key);
        var safeFileName = SanitizeFileName(fileName);
        return $"{dir}/{safeFileName}";
    }

    /// <summary>
    /// Gets a sharded defect object path including the "defects" prefix and sanitized key subfolder.
    /// Example: key "PROD-1", fileName "feat.json" -> "defects/20/3a/bc/4f/PROD-1/feat.json"
    /// </summary>
    public static string GetShardedDefectPath(string keyId, string fileName)
    {
        var dir = GetShardedDirectory(keyId);
        var safeKey = SanitizeFolderSegment(keyId);
        var safeFileName = SanitizeFileName(fileName);
        return $"defects/{dir}/{safeKey}/{safeFileName}";
    }

    /// <summary>
    /// Gets the standard bundle path for a product's defects and images.
    /// Example: key "PROD-1" -> "defects/20/3a/bc/4f/PROD-1_bundle.zip"
    /// </summary>
    public static string GetDefectBundlePath(string keyId)
    {
        var dir = GetShardedDirectory(keyId);
        var safeKey = SanitizeFolderSegment(keyId);
        return $"defects/{dir}/{safeKey}_bundle.zip";
    }

    /// <summary>
    /// Gets the standard consolidated defect pack path for a product's defects.
    /// Example: key "824334" -> "defects/20/3a/bc/4f/824334_defects.json.gz"
    /// </summary>
    public static string GetDefectPackPath(string keyId)
    {
        var dir = GetShardedDirectory(keyId);
        var safeKey = SanitizeFolderSegment(keyId);
        return $"defects/{dir}/{safeKey}_defects.json.gz";
    }

    private static string SanitizeFolderSegment(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "default";
        var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        var sanitized = new string(chars);
        return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return $"{Guid.NewGuid():N}.bin";
        var parts = fileName.Trim().Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var leaf = parts[^1];
        var ext = Path.GetExtension(leaf);
        var baseName = Path.GetFileNameWithoutExtension(leaf);
        var chars = baseName.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
        var safeBase = new string(chars);
        if (string.IsNullOrWhiteSpace(safeBase)) safeBase = Guid.NewGuid().ToString("N");
        return string.IsNullOrWhiteSpace(ext) ? safeBase : $"{safeBase}{ext}";
    }
}
