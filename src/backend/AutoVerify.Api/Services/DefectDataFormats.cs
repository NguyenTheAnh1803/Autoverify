namespace AutoVerify.Api.Services;

/// <summary>
/// Canonical format constants and normalizer for defect stream decoding.
/// Officially supported canonical formats: auto, json, jsonl, gzip, zip.
/// </summary>
public static class DefectDataFormats
{
    public const string Auto = "auto";
    public const string Json = "json";
    public const string Jsonl = "jsonl";
    public const string Gzip = "gzip";
    public const string Zip = "zip";

    public static readonly string[] AllCanonical = [Auto, Json, Jsonl, Gzip, Zip];

    public static bool TryNormalize(string? raw, out string canonical)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            canonical = Auto;
            return true;
        }

        var normalized = raw.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "");

        switch (normalized)
        {
            case "auto":
                canonical = Auto;
                return true;

            case "json":
                canonical = Json;
                return true;

            case "jsonl":
            case "ndjson":
            case "jsonlines":
            case "rawndjson":
            case "raw_ndjson":
                canonical = Jsonl;
                return true;

            case "gzip":
            case "gz":
            case "defect_pack_gz":
            case "defectpackgz":
            case "defectpackjson":
            case "defect_pack_json":
                canonical = Gzip;
                return true;

            case "zip":
            case "bundle_zip":
            case "bundlezip":
            case "product_bundle_zip":
                canonical = Zip;
                return true;

            case "storagefile":
            case "storage_file":
                canonical = Auto;
                return true;

            default:
                canonical = string.Empty;
                return false;
        }
    }

    public static bool IsValidFormat(string? raw) => TryNormalize(raw, out _);

    public static string NormalizeOrDefault(string? raw, string fallback = Auto)
    {
        return TryNormalize(raw, out var canonical) ? canonical : fallback;
    }
}
