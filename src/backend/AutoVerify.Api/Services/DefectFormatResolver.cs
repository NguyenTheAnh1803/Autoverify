using System.Text;

namespace AutoVerify.Api.Services;

/// <summary>
/// Metadata passed from StorageStreamResult or request context into the format resolver and parser.
/// </summary>
public sealed record StreamMetadata(
    string? SourceUri = null,
    string? ContentType = null,
    string? ContentEncoding = null,
    long? ContentLength = null,
    string? FormatHint = null
);

/// <summary>
/// Deterministic format resolver for defect streaming pipelines.
/// Resolution priority order:
/// 1. Explicit canonical or legacy format hint (if specified and not 'auto')
/// 2. Magic bytes: GZIP (0x1F, 0x8B), ZIP (0x50, 0x4B)
/// 3. HTTP / Storage metadata: ContentEncoding, ContentType, URI / filename extension
/// 4. Text stream sniffing: JSON array / object vs. JSONL / NDJSON
/// </summary>
public static class DefectFormatResolver
{
    public static string ResolveFormat(ReadOnlySpan<byte> magicBytes, StreamMetadata? metadata)
    {
        // 1. Magic bytes for binary compressed formats (GZIP, ZIP) take precedence because
        // compressed binary archives cannot be parsed directly by text parsers.
        if (magicBytes.Length >= 2)
        {
            // GZIP: 1F 8B
            if (magicBytes[0] == 0x1F && magicBytes[1] == 0x8B)
            {
                return DefectDataFormats.Gzip;
            }

            // ZIP: 50 4B (PK)
            if (magicBytes[0] == 0x50 && magicBytes[1] == 0x4B)
            {
                return DefectDataFormats.Zip;
            }
        }

        // 2. Explicit hint if provided and not auto
        if (!string.IsNullOrWhiteSpace(metadata?.FormatHint) &&
            DefectDataFormats.TryNormalize(metadata.FormatHint, out var canonicalHint) &&
            canonicalHint != DefectDataFormats.Auto)
        {
            return canonicalHint;
        }

        // 3. Storage / HTTP metadata: ContentEncoding
        if (!string.IsNullOrWhiteSpace(metadata?.ContentEncoding) &&
            metadata.ContentEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
        {
            return DefectDataFormats.Gzip;
        }

        // 3. Storage / HTTP metadata: ContentType
        if (!string.IsNullOrWhiteSpace(metadata?.ContentType))
        {
            var ct = metadata.ContentType.Trim().ToLowerInvariant();
            if (ct.Contains("gzip") || ct.Contains("x-gzip"))
            {
                return DefectDataFormats.Gzip;
            }
            if (ct.Contains("zip") || ct.Contains("x-zip-compressed"))
            {
                return DefectDataFormats.Zip;
            }
            if (ct.Contains("ndjson") || ct.Contains("jsonl") || ct.Contains("jsonlines") || ct.Contains("x-ndjson"))
            {
                return DefectDataFormats.Jsonl;
            }
        }

        // 3. Clean filename / URI extension
        if (!string.IsNullOrWhiteSpace(metadata?.SourceUri))
        {
            var cleanUri = metadata.SourceUri.Split('?')[0].Split('#')[0].Trim().ToLowerInvariant();
            if (cleanUri.EndsWith(".zip"))
            {
                return DefectDataFormats.Zip;
            }
            if (cleanUri.EndsWith(".gz") || cleanUri.EndsWith(".tar.gz"))
            {
                return DefectDataFormats.Gzip;
            }
            if (cleanUri.EndsWith(".jsonl") || cleanUri.EndsWith(".ndjson"))
            {
                return DefectDataFormats.Jsonl;
            }
            if (cleanUri.EndsWith(".json"))
            {
                // Can be json or jsonl - check text sniffing
            }
        }

        // 4. Text stream sniffing (JSON array/object vs JSONL/NDJSON)
        var sniffed = SniffTextFormat(magicBytes);
        if (sniffed != null)
        {
            return sniffed;
        }

        // Default fallback
        if (!string.IsNullOrWhiteSpace(metadata?.SourceUri))
        {
            var cleanUri = metadata.SourceUri.Split('?')[0].Split('#')[0].Trim().ToLowerInvariant();
            if (cleanUri.EndsWith(".json"))
            {
                return DefectDataFormats.Json;
            }
        }

        return DefectDataFormats.Json;
    }

    /// <summary>
    /// Sniffs the leading UTF-8 text bytes to distinguish between JSON and JSONL.
    /// </summary>
    public static string? SniffTextFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return null;

        var span = bytes;

        // Strip UTF-8 BOM if present
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }

        // Skip leading whitespace
        var startIdx = 0;
        while (startIdx < span.Length && char.IsWhiteSpace((char)span[startIdx]))
        {
            startIdx++;
        }

        if (startIdx >= span.Length) return null;

        var firstChar = (char)span[startIdx];

        // Root array e.g. [ { ... }, ... ]
        if (firstChar == '[')
        {
            return DefectDataFormats.Json;
        }

        // Starts with '{' - check if multi-line JSONL or standard JSON
        if (firstChar == '{')
        {
            string text;
            try
            {
                text = Encoding.UTF8.GetString(span[startIdx..]);
            }
            catch
            {
                return DefectDataFormats.Json;
            }

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length > 1)
            {
                var line1 = lines[0].Trim();
                var line2 = lines[1].Trim();

                // If line 1 is an object (or ends with }) and line 2 starts with another object {, it's JSONL
                if (line2.StartsWith('{') && (line1.EndsWith('}') || line1.Contains('}')))
                {
                    return DefectDataFormats.Jsonl;
                }
            }

            // Check if wrapper object with array properties: "defects", "items", "rows", "data", "records"
            string[] wrapperMarkers = ["\"defects\"", "\"items\"", "\"rows\"", "\"data\"", "\"records\""];
            foreach (var marker in wrapperMarkers)
            {
                if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return DefectDataFormats.Json;
                }
            }

            // Single object or root object -> json
            return DefectDataFormats.Json;
        }

        return null;
    }
}
