using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AutoVerify.Api.Persistence.Entities;

namespace AutoVerify.Api.Services;

/// <summary>
/// Safe exception thrown when defect stream decoding or parsing fails.
/// Carries stage, format, entry name, and row index without leaking internal stack traces.
/// </summary>
public class DefectParseException : Exception
{
    public string Stage { get; }
    public string Format { get; }
    public string? EntryName { get; }
    public int? RowIndex { get; }
    public long? ByteOffset { get; }
    public string? ErrorContext { get; }
    public string? LikelyCause { get; }
    public string? ActionMessage { get; }

    public DefectParseException(
        string message,
        string stage = "Parse",
        string format = "unknown",
        string? entryName = null,
        int? rowIndex = null,
        long? byteOffset = null,
        string? errorContext = null,
        string? likelyCause = null,
        string? actionMessage = null,
        Exception? innerException = null)
        : base(FormatSafeMessage(message, stage, format, entryName, rowIndex, byteOffset, errorContext, likelyCause, actionMessage), innerException)
    {
        Stage = stage;
        Format = format;
        EntryName = entryName;
        RowIndex = rowIndex;
        ByteOffset = byteOffset;
        ErrorContext = errorContext;
        LikelyCause = likelyCause;
        ActionMessage = actionMessage;
    }

    private static string FormatSafeMessage(
        string message,
        string stage,
        string format,
        string? entryName,
        int? rowIndex,
        long? byteOffset,
        string? errorContext,
        string? likelyCause,
        string? actionMessage)
    {
        var sb = new StringBuilder();
        sb.Append($"[Stage: {stage}, Format: {format}");
        if (!string.IsNullOrWhiteSpace(entryName))
        {
            sb.Append($", Entry: '{Path.GetFileName(entryName)}'");
        }
        if (rowIndex.HasValue && rowIndex.Value > 0)
        {
            sb.Append($", Row: {rowIndex.Value}");
        }
        sb.Append($"] {message}");

        if (byteOffset.HasValue && byteOffset.Value >= 0)
        {
            sb.Append($" | ByteOffset: {byteOffset.Value}");
        }
        if (!string.IsNullOrWhiteSpace(errorContext))
        {
            sb.Append($" | Context: '{errorContext}'");
        }
        if (!string.IsNullOrWhiteSpace(likelyCause))
        {
            sb.Append($" | LikelyCause: {likelyCause}");
        }
        if (!string.IsNullOrWhiteSpace(actionMessage))
        {
            sb.Append($" | Action: {actionMessage}");
        }

        return sb.ToString();
    }
}

/// <summary>
/// Safeguards against zip-slip and zip-bomb attacks during archive unpacking.
/// </summary>
public sealed class ZipSafeguardOptions
{
    public int MaxEntries { get; set; } = 1000;
    public long MaxEntryUncompressedBytes { get; set; } = 250 * 1024 * 1024; // 250MB
    public long MaxTotalUncompressedBytes { get; set; } = 500 * 1024 * 1024; // 500MB
    public double MaxCompressionRatio { get; set; } = 100.0;
    public long MaxZipFileSizeBytes { get; set; } = 500 * 1024 * 1024; // 500MB
}

public static class DefectStreamParser
{
    private static readonly string[] DefectArrayPropertyNames = ["defects", "Defects", "items", "Items", "rows", "Rows", "data", "Data", "records", "Records"];

    /// <summary>
    /// Backward-compatible overload for existing callers and tests.
    /// </summary>
    public static IAsyncEnumerable<DefectPreviewRecordEntity> ParseDefectsAsync(
        Stream rawStream,
        string? sourceUri,
        string? formatHint,
        int maxSnippetLength = 500,
        CancellationToken ct = default)
    {
        return ParseDefectsAsync(
            rawStream,
            new StreamMetadata(SourceUri: sourceUri, FormatHint: formatHint),
            onFormatResolved: null,
            onStageChanged: null,
            onWarning: null,
            maxSnippetLength: maxSnippetLength,
            maxRowsLimit: 50000,
            zipSafeguards: null,
            enableRecovery: true,
            ct: ct);
    }

    /// <summary>
    /// Overload without warning callback for backward compatibility.
    /// </summary>
    public static IAsyncEnumerable<DefectPreviewRecordEntity> ParseDefectsAsync(
        Stream rawStream,
        StreamMetadata metadata,
        Action<string>? onFormatResolved,
        Action<string>? onStageChanged,
        int maxSnippetLength,
        int maxRowsLimit,
        ZipSafeguardOptions? zipSafeguards = null,
        CancellationToken ct = default)
    {
        return ParseDefectsAsync(
            rawStream,
            metadata,
            onFormatResolved,
            onStageChanged,
            onWarning: null,
            maxSnippetLength: maxSnippetLength,
            maxRowsLimit: maxRowsLimit,
            zipSafeguards: zipSafeguards,
            enableRecovery: true,
            ct: ct);
    }

    /// <summary>
    /// Primary streaming defect decoder and parser.
    /// Pipeline: stream -> detect format -> decompress GZIP/ZIP -> parse JSON/JSONL -> normalize records.
    /// </summary>
    public static async IAsyncEnumerable<DefectPreviewRecordEntity> ParseDefectsAsync(
        Stream rawStream,
        StreamMetadata metadata,
        Action<string>? onFormatResolved = null,
        Action<string>? onStageChanged = null,
        Action<string>? onWarning = null,
        int maxSnippetLength = 500,
        int maxRowsLimit = 50000,
        ZipSafeguardOptions? zipSafeguards = null,
        bool enableRecovery = true,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rawStream);

        onStageChanged?.Invoke("Detecting");

        // Wrap raw stream in stream-safe peek wrapper (peeking 2048 bytes without consuming/seeking stream)
        var peekable = await PeekableStream.CreateAsync(rawStream, maxPeekBytes: 2048, leaveOpen: true, ct: ct);

        // Resolve format deterministically using hint -> magic bytes -> metadata -> text sniffing
        var detectedFormat = DefectFormatResolver.ResolveFormat(peekable.PeekedSpan, metadata);
        onFormatResolved?.Invoke(detectedFormat);

        var safeguards = zipSafeguards ?? new ZipSafeguardOptions();

        switch (detectedFormat)
        {
            case DefectDataFormats.Zip:
                onStageChanged?.Invoke("DecompressingZip");
                await foreach (var item in ParseZipAsync(peekable, metadata, onStageChanged, maxSnippetLength, maxRowsLimit, safeguards, ct))
                {
                    yield return item;
                }
                yield break;

            case DefectDataFormats.Gzip:
                onStageChanged?.Invoke("DecompressingGzip");
                await foreach (var item in ParseGzipAsync(peekable, metadata, onFormatResolved, onStageChanged, onWarning, maxSnippetLength, maxRowsLimit, enableRecovery, ct))
                {
                    yield return item;
                }
                yield break;

            case DefectDataFormats.Jsonl:
                onStageChanged?.Invoke("ParsingJsonl");
                await foreach (var item in ParseJsonlStreamAsync(peekable, metadata.SourceUri ?? string.Empty, entryName: null, maxSnippetLength, maxRowsLimit, ct))
                {
                    yield return item;
                }
                yield break;

            case DefectDataFormats.Json:
            default:
                onStageChanged?.Invoke("ParsingJson");
                await foreach (var item in ParseJsonStreamAsync(peekable, metadata.SourceUri ?? string.Empty, entryName: null, onWarning, maxSnippetLength, maxRowsLimit, enableRecovery, ct))
                {
                    yield return item;
                }
                yield break;
        }
    }

    private static async IAsyncEnumerable<DefectPreviewRecordEntity> ParseGzipAsync(
        Stream gzipStream,
        StreamMetadata metadata,
        Action<string>? onFormatResolved,
        Action<string>? onStageChanged,
        Action<string>? onWarning,
        int maxSnippetLength,
        int maxRowsLimit,
        bool enableRecovery,
        [EnumeratorCancellation] CancellationToken ct)
    {
        GZipStream? gz = null;
        Stream? decompressedStream = null;
        IAsyncDisposable? cleanup = null;

        try
        {
            try
            {
                gz = new GZipStream(gzipStream, CompressionMode.Decompress, leaveOpen: true);
                (decompressedStream, cleanup) = await EnsureSeekableStreamAsync(gz, ct);
            }
            catch (Exception ex) when (ex is not DefectParseException)
            {
                throw new DefectParseException(
                    $"Failed to decompress GZIP stream: {ex.Message}",
                    stage: "DecompressGzip",
                    format: DefectDataFormats.Gzip,
                    entryName: metadata.SourceUri,
                    innerException: ex);
            }

            var peekBytes = new byte[2048];
            var peekedCount = await decompressedStream.ReadAsync(peekBytes.AsMemory(0, peekBytes.Length), ct);
            decompressedStream.Position = 0;

            // Sniff the decompressed inner payload format
            var innerMetadata = metadata with { FormatHint = null };
            var innerFormat = DefectFormatResolver.ResolveFormat(peekBytes.AsSpan(0, peekedCount), innerMetadata);
            if (innerFormat is DefectDataFormats.Gzip or DefectDataFormats.Zip)
            {
                innerFormat = DefectFormatResolver.SniffTextFormat(peekBytes.AsSpan(0, peekedCount)) ?? DefectDataFormats.Json;
            }

            onStageChanged?.Invoke(innerFormat == DefectDataFormats.Jsonl ? "ParsingJsonl" : "ParsingJson");

            var parser = innerFormat == DefectDataFormats.Jsonl
                ? ParseJsonlStreamAsync(decompressedStream, metadata.SourceUri ?? string.Empty, entryName: metadata.SourceUri, maxSnippetLength, maxRowsLimit, ct)
                : ParseJsonStreamAsync(decompressedStream, metadata.SourceUri ?? string.Empty, entryName: metadata.SourceUri, onWarning, maxSnippetLength, maxRowsLimit, enableRecovery, ct);

            await foreach (var record in parser.WithCancellation(ct))
            {
                yield return record;
            }
        }
        finally
        {
            if (cleanup != null) await cleanup.DisposeAsync();
            if (gz != null) await gz.DisposeAsync();
        }
    }

    private static async IAsyncEnumerable<DefectPreviewRecordEntity> ParseZipAsync(
        Stream zipStream,
        StreamMetadata metadata,
        Action<string>? onStageChanged,
        int maxSnippetLength,
        int maxRowsLimit,
        ZipSafeguardOptions safeguards,
        [EnumeratorCancellation] CancellationToken ct)
    {
        string? tempFile = null;
        ZipArchive? archive = null;
        Stream? fileStreamToDispose = null;

        try
        {
            if (zipStream.CanSeek)
            {
                try
                {
                    archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
                }
                catch (Exception ex)
                {
                    throw new DefectParseException(
                        $"Failed to open ZIP archive: {ex.Message}",
                        stage: "DecompressZip",
                        format: DefectDataFormats.Zip,
                        entryName: metadata.SourceUri,
                        innerException: ex);
                }
            }
            else
            {
                tempFile = Path.Combine(Path.GetTempPath(), $"defect_preview_{Guid.NewGuid():N}.zip");
                var totalSpooled = 0L;
                var buffer = new byte[64 * 1024];

                await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
                {
                    while (true)
                    {
                        var read = await zipStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                        if (read == 0) break;
                        totalSpooled += read;
                        if (totalSpooled > safeguards.MaxZipFileSizeBytes)
                        {
                            throw new DefectParseException(
                                $"ZIP stream size exceeds maximum allowed file size of {safeguards.MaxZipFileSizeBytes / (1024 * 1024)}MB.",
                                stage: "DecompressZip",
                                format: DefectDataFormats.Zip,
                                entryName: metadata.SourceUri);
                        }
                        await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                }

                var readFs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
                fileStreamToDispose = readFs;
                archive = new ZipArchive(readFs, ZipArchiveMode.Read, leaveOpen: false);
            }

            // Safeguard 1: Entry count limit
            if (archive.Entries.Count > safeguards.MaxEntries)
            {
                throw new DefectParseException(
                    $"ZIP archive entry count ({archive.Entries.Count}) exceeds safeguard limit of {safeguards.MaxEntries}.",
                    stage: "DecompressZip",
                    format: DefectDataFormats.Zip,
                    entryName: metadata.SourceUri);
            }

            // Safeguard 2 & 3: Zip-slip and zip-bomb check
            long totalUncompressedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                var fullName = entry.FullName;
                if (fullName.Contains("..") || Path.IsPathRooted(fullName) || fullName.StartsWith('/') || fullName.StartsWith('\\'))
                {
                    throw new DefectParseException(
                        $"ZIP entry '{fullName}' contains illegal path traversal characters (ZipSlip protection).",
                        stage: "DecompressZip",
                        format: DefectDataFormats.Zip,
                        entryName: fullName);
                }

                if (entry.Length > safeguards.MaxEntryUncompressedBytes)
                {
                    throw new DefectParseException(
                        $"ZIP entry '{fullName}' uncompressed size ({entry.Length} bytes) exceeds safeguard limit of {safeguards.MaxEntryUncompressedBytes / (1024 * 1024)}MB.",
                        stage: "DecompressZip",
                        format: DefectDataFormats.Zip,
                        entryName: fullName);
                }

                totalUncompressedBytes += entry.Length;
                if (totalUncompressedBytes > safeguards.MaxTotalUncompressedBytes)
                {
                    throw new DefectParseException(
                        $"ZIP archive total uncompressed size exceeds safeguard limit of {safeguards.MaxTotalUncompressedBytes / (1024 * 1024)}MB.",
                        stage: "DecompressZip",
                        format: DefectDataFormats.Zip,
                        entryName: fullName);
                }

                if (entry.CompressedLength > 0 && entry.Length > 10 * 1024 * 1024)
                {
                    var ratio = (double)entry.Length / entry.CompressedLength;
                    if (ratio > safeguards.MaxCompressionRatio)
                    {
                        throw new DefectParseException(
                            $"ZIP entry '{fullName}' compression ratio ({ratio:F1}x) exceeds limit of {safeguards.MaxCompressionRatio}x (potential zip bomb).",
                            stage: "DecompressZip",
                            format: DefectDataFormats.Zip,
                            entryName: fullName);
                    }
                }
            }

            // Safe entry selection
            var validEntries = archive.Entries
                .Where(e => !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\') && !string.IsNullOrWhiteSpace(e.Name))
                .Where(e => !e.FullName.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase) &&
                            !e.Name.StartsWith("._", StringComparison.OrdinalIgnoreCase) &&
                            !e.Name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase) &&
                            !e.Name.StartsWith(".git", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var selectedEntries = SelectZipDataEntries(validEntries);

            onStageChanged?.Invoke("ParsingZipEntries");

            var totalYielded = 0;
            foreach (var entry in selectedEntries)
            {
                if (totalYielded >= maxRowsLimit) break;

                await using var entryStream = entry.Open();
                Stream dataStream = entryStream;
                GZipStream? gzStream = null;

                if (entry.Name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    gzStream = new GZipStream(entryStream, CompressionMode.Decompress);
                    dataStream = gzStream;
                }

                try
                {
                    await using var peekableEntry = await PeekableStream.CreateAsync(dataStream, maxPeekBytes: 2048, leaveOpen: true, ct: ct);
                    var entryMetadata = new StreamMetadata(SourceUri: entry.FullName, FormatHint: null);
                    var entryFormat = DefectFormatResolver.ResolveFormat(peekableEntry.PeekedSpan, entryMetadata);

                    var remainingLimit = maxRowsLimit - totalYielded;
                    var entryParser = entryFormat == DefectDataFormats.Jsonl
                        ? ParseJsonlStreamAsync(peekableEntry, metadata.SourceUri ?? entry.FullName, entry.FullName, maxSnippetLength, remainingLimit, ct)
                        : ParseJsonStreamAsync(peekableEntry, metadata.SourceUri ?? entry.FullName, entry.FullName, onWarning: null, maxSnippetLength, remainingLimit, enableRecovery: true, ct);

                    await foreach (var item in entryParser.WithCancellation(ct))
                    {
                        yield return item;
                        totalYielded++;
                        if (totalYielded >= maxRowsLimit) break;
                    }
                }
                finally
                {
                    if (gzStream != null) await gzStream.DisposeAsync();
                }
            }
        }
        finally
        {
            archive?.Dispose();
            if (fileStreamToDispose != null) await fileStreamToDispose.DisposeAsync();
            if (tempFile != null && File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    private static List<ZipArchiveEntry> SelectZipDataEntries(List<ZipArchiveEntry> entries)
    {
        var manifestEntry = entries.FirstOrDefault(e => e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        var explicitReferencedEntries = new List<ZipArchiveEntry>();

        if (manifestEntry != null)
        {
            try
            {
                using var s = manifestEntry.Open();
                using var doc = JsonDocument.Parse(s);
                var root = doc.RootElement;

                // Check if manifest explicitly lists data files or payload entries
                string[] manifestRefPropNames = ["files", "dataFiles", "data_files", "entries", "dataFile", "data_file", "payload", "recordsFile", "records_file"];
                foreach (var propName in manifestRefPropNames)
                {
                    if (root.TryGetProperty(propName, out var prop))
                    {
                        if (prop.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in prop.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    var fileName = item.GetString();
                                    if (!string.IsNullOrWhiteSpace(fileName))
                                    {
                                        var match = entries.FirstOrDefault(e => e.FullName.Equals(fileName, StringComparison.OrdinalIgnoreCase) || e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                                        if (match != null && !explicitReferencedEntries.Contains(match))
                                        {
                                            explicitReferencedEntries.Add(match);
                                        }
                                    }
                                }
                            }
                        }
                        else if (prop.ValueKind == JsonValueKind.String)
                        {
                            var fileName = prop.GetString();
                            if (!string.IsNullOrWhiteSpace(fileName))
                            {
                                var match = entries.FirstOrDefault(e => e.FullName.Equals(fileName, StringComparison.OrdinalIgnoreCase) || e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                                if (match != null && !explicitReferencedEntries.Contains(match))
                                {
                                    explicitReferencedEntries.Add(match);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // If manifest fails to parse, proceed to fallback selection
            }
        }

        if (explicitReferencedEntries.Count > 0)
        {
            return explicitReferencedEntries;
        }

        // Separate defect payload data entries (excluding manifest.json and per-defect feature entries)
        var candidateDataEntries = entries
            .Where(e => !e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            .Where(e => !e.Name.EndsWith("_feature.json", StringComparison.OrdinalIgnoreCase) &&
                        !e.Name.EndsWith("_custom_feature.json", StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Priority 1: dedicated defects/records/data payload entries
        var defectPayloadEntries = candidateDataEntries
            .Where(e => e.Name.Contains("defect", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.Contains("record", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.Contains("data", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Name)
            .ToList();

        if (defectPayloadEntries.Count > 0)
        {
            return defectPayloadEntries;
        }

        // Priority 2: line-delimited entries
        var lineDelimitedEntries = candidateDataEntries
            .Where(e => e.Name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Name)
            .ToList();

        if (lineDelimitedEntries.Count > 0)
        {
            return lineDelimitedEntries;
        }

        // Priority 3: other JSON entries (excluding manifest)
        if (candidateDataEntries.Count > 0)
        {
            return candidateDataEntries.OrderBy(e => e.Name).ToList();
        }

        // Priority 4: if manifest.json exists and no other defect data files exist,
        // parse manifest.json itself (e.g. UploadProductDefectBundleAsync bundles defects inside manifest.json)
        if (manifestEntry != null)
        {
            return [manifestEntry];
        }

        return [];
    }

    private static async IAsyncEnumerable<DefectPreviewRecordEntity> ParseJsonlStreamAsync(
        Stream stream,
        string sourceUri,
        string? entryName,
        int maxSnippetLength,
        int maxRowsLimit,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: true);
        var lineIndex = 0;
        var rowCount = 0;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            lineIndex++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                throw new DefectParseException(
                    $"Invalid JSON syntax at line {lineIndex}: {ex.Message}",
                    stage: "ParseJsonl",
                    format: DefectDataFormats.Jsonl,
                    entryName: entryName ?? sourceUri,
                    rowIndex: lineIndex,
                    innerException: ex);
            }

            try
            {
                yield return ConvertJsonElementToRecord(doc.RootElement, sourceUri, maxSnippetLength);
                rowCount++;
                if (rowCount >= maxRowsLimit) yield break;
            }
            finally
            {
                doc.Dispose();
            }
        }
    }

    public static async Task<(Stream Stream, IAsyncDisposable? Cleanup)> EnsureSeekableStreamAsync(Stream stream, CancellationToken ct)
    {
        if (stream.CanSeek)
        {
            return (stream, null);
        }

        var ms = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0) break;
                await ms.WriteAsync(buffer.AsMemory(0, read), ct);

                if (ms.Length > 20 * 1024 * 1024) // Spool to disk if > 20MB
                {
                    var tempFile = Path.Combine(Path.GetTempPath(), $"defect_stream_{Guid.NewGuid():N}.tmp");
                    var fs = new FileStream(tempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.DeleteOnClose);
                    ms.Position = 0;
                    await ms.CopyToAsync(fs, ct);
                    await ms.DisposeAsync();

                    while (true)
                    {
                        var fileRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                        if (fileRead == 0) break;
                        await fs.WriteAsync(buffer.AsMemory(0, fileRead), ct);
                    }

                    fs.Position = 0;
                    return (fs, fs);
                }
            }

            ms.Position = 0;
            return (ms, ms);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static string ExtractSafeErrorContext(Stream stream, long byteOffset, int windowSize = 40)
    {
        if (!stream.CanSeek) return string.Empty;

        var originalPos = stream.Position;
        try
        {
            var start = Math.Max(0, byteOffset - windowSize);
            var length = (int)Math.Min(windowSize * 2, stream.Length - start);
            if (length <= 0) return string.Empty;

            stream.Position = start;
            var buffer = new byte[length];
            var read = stream.Read(buffer, 0, length);
            if (read <= 0) return string.Empty;

            var text = Encoding.UTF8.GetString(buffer, 0, read);
            text = text.Replace("\r", " ").Replace("\n", " ");
            text = RedactSensitiveData(text);

            var prefix = start > 0 ? "..." : string.Empty;
            var suffix = (start + read < stream.Length) ? "..." : string.Empty;
            return $"{prefix}{text}{suffix}";
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            stream.Position = originalPos;
        }
    }

    private static string RedactSensitiveData(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sensitivePatterns = new[] { "password", "token", "secret", "apikey", "api_key", "auth" };
        var result = text;
        foreach (var pattern in sensitivePatterns)
        {
            var idx = 0;
            while ((idx = result.IndexOf(pattern, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var colonIdx = result.IndexOf(':', idx);
                if (colonIdx > idx && colonIdx < idx + pattern.Length + 10)
                {
                    var valStart = colonIdx + 1;
                    while (valStart < result.Length && (result[valStart] == ' ' || result[valStart] == '"'))
                    {
                        valStart++;
                    }
                    var valEnd = valStart;
                    while (valEnd < result.Length && result[valEnd] != '"' && result[valEnd] != ',' && result[valEnd] != '}')
                    {
                        valEnd++;
                    }
                    if (valEnd > valStart)
                    {
                        result = result.Substring(0, valStart) + "***" + result.Substring(valEnd);
                    }
                }
                idx += pattern.Length;
            }
        }
        return result;
    }

    public static (string LikelyCause, string ActionMessage) AnalyzeSyntaxFault(string message, string context)
    {
        if (context.Contains(",,") || message.Contains("',' is an invalid start of a property name"))
        {
            return (
                "Repeated comma or misplaced delimiter outside string literal",
                "Verify defect export generator for empty records or repeated delimiters, or enable safe JSON delimiter recovery."
            );
        }
        if (context.Contains(",}") || context.Contains(",]") || context.Contains(", }") || context.Contains(", ]"))
        {
            return (
                "Unexpected trailing comma before container closing delimiter",
                "Remove trailing comma before '}' or ']' in JSON source export."
            );
        }
        if (context.Contains("{,") || context.Contains("[,") || context.Contains("{ ,") || context.Contains("[ ,"))
        {
            return (
                "Unexpected leading comma after container opening delimiter",
                "Remove leading comma after '{' or '[' in JSON source export."
            );
        }
        if (message.Contains("Expected a '\"'"))
        {
            return (
                "Malformed property name syntax",
                "Ensure all object property keys are enclosed in valid double quotes."
            );
        }
        if (message.Contains("end of data") || message.Contains("EOF"))
        {
            return (
                "Truncated JSON payload or premature end of stream",
                "Ensure defect payload was fully transferred and decompressed."
            );
        }
        return (
            "Invalid JSON syntax",
            "Verify JSON document structure against standard JSON specification."
        );
    }

    public static bool IsUnambiguousJsonl(Stream stream, out int lineCount)
    {
        lineCount = 0;
        if (!stream.CanSeek) return false;

        var originalPos = stream.Position;
        try
        {
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024, leaveOpen: true);
            string? line;
            var validLines = 0;

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var trimmed = line.Trim();
                if (!((trimmed.StartsWith('{') && trimmed.EndsWith('}')) || (trimmed.StartsWith('[') && trimmed.EndsWith(']'))))
                {
                    return false;
                }

                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    validLines++;
                }
                catch (JsonException)
                {
                    return false;
                }
            }

            lineCount = validLines;
            return validLines >= 2;
        }
        finally
        {
            stream.Position = originalPos;
        }
    }

    public static class JsonDelimiterNormalizer
    {
        public static byte[] Normalize(ReadOnlySpan<byte> input, out int repairsCount)
        {
            repairsCount = 0;
            var output = new byte[input.Length];
            var outLen = 0;
            var inString = false;
            var isEscaped = false;

            var lastStructuralToken = (byte)0;
            var lastCommaOutputIndex = -1;

            for (var i = 0; i < input.Length; i++)
            {
                var b = input[i];

                if (inString)
                {
                    output[outLen++] = b;
                    if (isEscaped)
                    {
                        isEscaped = false;
                    }
                    else if (b == (byte)'\\')
                    {
                        isEscaped = true;
                    }
                    else if (b == (byte)'"')
                    {
                        inString = false;
                        lastStructuralToken = (byte)'v';
                    }
                    continue;
                }

                // Outside string
                if (b == (byte)'"')
                {
                    inString = true;
                    isEscaped = false;
                    lastStructuralToken = (byte)'v';
                    lastCommaOutputIndex = -1;
                    output[outLen++] = b;
                    continue;
                }

                // Whitespace: pass through
                if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                {
                    output[outLen++] = b;
                    continue;
                }

                if (b == (byte)',')
                {
                    // Repeated comma outside string
                    if (lastStructuralToken == (byte)',')
                    {
                        repairsCount++;
                        continue;
                    }

                    // Leading comma after { or [
                    if (lastStructuralToken is (byte)'{' or (byte)'[')
                    {
                        repairsCount++;
                        continue;
                    }

                    lastStructuralToken = (byte)',';
                    lastCommaOutputIndex = outLen;
                    output[outLen++] = b;
                    continue;
                }

                if (b is (byte)'}' or (byte)']')
                {
                    // Trailing comma before } or ]
                    if (lastStructuralToken == (byte)',' && lastCommaOutputIndex >= 0)
                    {
                        output[lastCommaOutputIndex] = (byte)' ';
                        repairsCount++;
                        lastStructuralToken = b;
                        lastCommaOutputIndex = -1;
                        output[outLen++] = b;
                        continue;
                    }

                    lastStructuralToken = b;
                    lastCommaOutputIndex = -1;
                    output[outLen++] = b;
                    continue;
                }

                if (b is (byte)'{' or (byte)'[' or (byte)':')
                {
                    lastStructuralToken = b;
                    lastCommaOutputIndex = -1;
                    output[outLen++] = b;
                    continue;
                }

                lastStructuralToken = (byte)'v';
                lastCommaOutputIndex = -1;
                output[outLen++] = b;
            }

            if (outLen == input.Length)
            {
                return output;
            }

            var result = new byte[outLen];
            Array.Copy(output, result, outLen);
            return result;
        }
    }

    private static async IAsyncEnumerable<DefectPreviewRecordEntity> ParseJsonStreamAsync(
        Stream stream,
        string sourceUri,
        string? entryName,
        Action<string>? onWarning,
        int maxSnippetLength,
        int maxRowsLimit,
        bool enableRecovery,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var (seekableStream, cleanup) = await EnsureSeekableStreamAsync(stream, ct);
        try
        {
            var initialPosition = seekableStream.Position;
            Stream streamToIterate = seekableStream;

            if (enableRecovery && seekableStream.CanSeek)
            {
                var probeFailed = false;
                var probeCount = 0;
                DefectParseException? probeException = null;

                try
                {
                    await foreach (var _ in ParseJsonStreamStrictAsync(seekableStream, sourceUri, entryName, maxSnippetLength, maxRowsLimit, ct))
                    {
                        probeCount++;
                    }
                }
                catch (DefectParseException ex)
                {
                    probeFailed = true;
                    probeException = ex;
                }

                seekableStream.Position = initialPosition;

                if (probeFailed || probeCount == 0)
                {
                    using var ms = new MemoryStream();
                    await seekableStream.CopyToAsync(ms, ct);
                    var rawBytes = ms.ToArray();

                    var normalizedBytes = JsonDelimiterNormalizer.Normalize(rawBytes, out var repairsCount);
                    MemoryStream? recoveredStream = null;
                    if (repairsCount > 0)
                    {
                        var testMs = new MemoryStream(normalizedBytes);
                        var testOk = false;
                        try
                        {
                            await foreach (var _ in ParseJsonStreamStrictAsync(testMs, sourceUri, entryName, maxSnippetLength, maxRowsLimit, ct))
                            {
                            }
                            testOk = true;
                        }
                        catch
                        {
                            testOk = false;
                        }

                        if (testOk)
                        {
                            testMs.Position = 0;
                            recoveredStream = testMs;
                            onWarning?.Invoke($"Recovered malformed JSON syntax: normalized {repairsCount} misplaced/repeated comma delimiter(s) outside strings.");
                        }
                        else
                        {
                            await testMs.DisposeAsync();
                        }
                    }

                    if (recoveredStream != null)
                    {
                        streamToIterate = recoveredStream;
                    }
                    else
                    {
                        seekableStream.Position = initialPosition;
                        if (IsUnambiguousJsonl(seekableStream, out var jsonlCount))
                        {
                            seekableStream.Position = initialPosition;
                            onWarning?.Invoke($"Detected line-delimited JSON ({jsonlCount} lines) mislabelled as JSON; recovered with streaming JSONL parser.");
                            await foreach (var record in ParseJsonlStreamAsync(seekableStream, sourceUri, entryName, maxSnippetLength, maxRowsLimit, ct))
                            {
                                yield return record;
                            }
                            yield break;
                        }

                        if (probeException != null)
                        {
                            throw probeException;
                        }
                    }
                }
            }

            seekableStream.Position = initialPosition;
            try
            {
                await foreach (var record in ParseJsonStreamStrictAsync(streamToIterate, sourceUri, entryName, maxSnippetLength, maxRowsLimit, ct))
                {
                    yield return record;
                }
            }
            finally
            {
                if (!ReferenceEquals(streamToIterate, seekableStream))
                {
                    await streamToIterate.DisposeAsync();
                }
            }
        }
        finally
        {
            if (cleanup != null)
            {
                await cleanup.DisposeAsync();
            }
        }
    }

    private static async IAsyncEnumerable<DefectPreviewRecordEntity> ParseJsonStreamStrictAsync(
        Stream stream,
        string sourceUri,
        string? entryName,
        int maxSnippetLength,
        int maxRowsLimit,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var pipeReader = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 64 * 1024, leaveOpen: true));
        var readerState = new JsonReaderState(new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = false
        });

        var inTargetArray = false;
        var targetArrayDepth = -1;
        var lookingForArrayValue = false;
        var recordsBatch = new List<DefectPreviewRecordEntity>(64);
        var totalYielded = 0;

        try
        {
            while (true)
            {
                var readResult = await pipeReader.ReadAsync(ct);
                var buffer = readResult.Buffer;

                if (buffer.IsEmpty && readResult.IsCompleted)
                {
                    break;
                }

                recordsBatch.Clear();

                SequencePosition consumed;
                JsonReaderState newState;

                try
                {
                    (consumed, newState) = ParseChunk(
                        buffer,
                        readResult.IsCompleted,
                        readerState,
                        ref inTargetArray,
                        ref targetArrayDepth,
                        ref lookingForArrayValue,
                        sourceUri,
                        entryName,
                        maxSnippetLength,
                        recordsBatch);
                }
                catch (JsonException ex)
                {
                    var byteOffset = ex.BytePositionInLine ?? 0;
                    var context = ExtractSafeErrorContext(stream, byteOffset);
                    var (likelyCause, actionMessage) = AnalyzeSyntaxFault(ex.Message, context);

                    throw new DefectParseException(
                        $"Invalid JSON syntax: {ex.Message}",
                        stage: "ParseJson",
                        format: DefectDataFormats.Json,
                        entryName: entryName ?? sourceUri,
                        rowIndex: totalYielded + recordsBatch.Count + 1,
                        byteOffset: byteOffset,
                        errorContext: context,
                        likelyCause: likelyCause,
                        actionMessage: actionMessage,
                        innerException: ex);
                }

                readerState = newState;
                pipeReader.AdvanceTo(consumed, buffer.End);

                for (var i = 0; i < recordsBatch.Count; i++)
                {
                    yield return recordsBatch[i];
                    totalYielded++;
                    if (totalYielded >= maxRowsLimit) yield break;
                }

                if (readResult.IsCompleted)
                {
                    break;
                }
            }

            // Single JSON object fallback if no array was encountered
            if (totalYielded == 0 && !inTargetArray && stream.CanSeek)
            {
                stream.Position = 0;
                JsonDocument? singleDoc = null;
                try
                {
                    singleDoc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                }
                catch (JsonException ex)
                {
                    var byteOffset = ex.BytePositionInLine ?? 0;
                    var context = ExtractSafeErrorContext(stream, byteOffset);
                    var (likelyCause, actionMessage) = AnalyzeSyntaxFault(ex.Message, context);

                    throw new DefectParseException(
                        $"Invalid JSON syntax in single object: {ex.Message}",
                        stage: "ParseJson",
                        format: DefectDataFormats.Json,
                        entryName: entryName ?? sourceUri,
                        rowIndex: 1,
                        byteOffset: byteOffset,
                        errorContext: context,
                        likelyCause: likelyCause,
                        actionMessage: actionMessage,
                        innerException: ex);
                }

                try
                {
                    if (singleDoc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var hasWrapper = false;
                        foreach (var prop in singleDoc.RootElement.EnumerateObject())
                        {
                            if (DefectArrayPropertyNames.Any(p => string.Equals(p, prop.Name, StringComparison.OrdinalIgnoreCase)) &&
                                prop.Value.ValueKind == JsonValueKind.Array)
                            {
                                hasWrapper = true;
                                foreach (var item in prop.Value.EnumerateArray())
                                {
                                    if (item.ValueKind == JsonValueKind.Object)
                                    {
                                        yield return ConvertJsonElementToRecord(item, sourceUri, maxSnippetLength);
                                        totalYielded++;
                                        if (totalYielded >= maxRowsLimit) yield break;
                                    }
                                }
                                break;
                            }
                        }

                        if (!hasWrapper)
                        {
                            yield return ConvertJsonElementToRecord(singleDoc.RootElement, sourceUri, maxSnippetLength);
                        }
                    }
                }
                finally
                {
                    singleDoc?.Dispose();
                }
            }
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static (SequencePosition Consumed, JsonReaderState State) ParseChunk(
        ReadOnlySequence<byte> buffer,
        bool isFinalBlock,
        JsonReaderState state,
        ref bool inTargetArray,
        ref int targetArrayDepth,
        ref bool lookingForArrayValue,
        string sourceUri,
        string? entryName,
        int maxSnippetLength,
        List<DefectPreviewRecordEntity> outRecords)
    {
        var reader = new Utf8JsonReader(buffer, isFinalBlock: isFinalBlock, state);
        var consumed = buffer.Start;
        var lastGoodState = state;

        while (true)
        {
            if (!inTargetArray)
            {
                if (!reader.Read())
                {
                    break;
                }

                if (lookingForArrayValue)
                {
                    if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        inTargetArray = true;
                        targetArrayDepth = reader.CurrentDepth;
                        lookingForArrayValue = false;
                        consumed = reader.Position;
                        lastGoodState = reader.CurrentState;
                        continue;
                    }
                    lookingForArrayValue = false;
                }

                if (reader.TokenType == JsonTokenType.StartArray && (reader.CurrentDepth == 0 || reader.CurrentDepth == 1))
                {
                    // Root array: [ {...}, {...} ]
                    inTargetArray = true;
                    targetArrayDepth = reader.CurrentDepth;
                    consumed = reader.Position;
                    lastGoodState = reader.CurrentState;
                    continue;
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var prop = reader.GetString();
                    if (DefectArrayPropertyNames.Any(p => string.Equals(p, prop, StringComparison.OrdinalIgnoreCase)))
                    {
                        lookingForArrayValue = true;
                    }
                }

                consumed = reader.Position;
                lastGoodState = reader.CurrentState;
            }
            else
            {
                if (!reader.Read())
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth <= targetArrayDepth)
                {
                    inTargetArray = false;
                    consumed = reader.Position;
                    lastGoodState = reader.CurrentState;
                    break;
                }

                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    if (JsonDocument.TryParseValue(ref reader, out var doc))
                    {
                        try
                        {
                            outRecords.Add(ConvertJsonElementToRecord(doc.RootElement, sourceUri, maxSnippetLength));
                        }
                        finally
                        {
                            doc.Dispose();
                        }

                        consumed = reader.Position;
                        lastGoodState = reader.CurrentState;
                    }
                    else
                    {
                        // Object was incomplete in buffer - keep consumed and state at the previous successful token
                        break;
                    }
                }
                else
                {
                    consumed = reader.Position;
                    lastGoodState = reader.CurrentState;
                }
            }
        }

        return (consumed, lastGoodState);
    }

    public static DefectPreviewRecordEntity ConvertJsonElementToRecord(
        JsonElement root,
        string? sourceUri,
        int maxSnippetLength = 500)
    {
        var record = new DefectPreviewRecordEntity();

        if (TryGetGuidProperty(root, out var parsedObsId, "observationId", "observation_id", "observationGuid"))
        {
            record.ObservationId = parsedObsId;
        }
        else
        {
            record.ObservationId = Guid.NewGuid();
        }

        record.DefectId = GetStringProperty(root, "id", "Id", "defectId", "defect_id", "DefectId", "ID");
        record.KeyId = GetStringProperty(root, "keyId", "key_id", "KeyId", "productId", "product_id", "fkId", "fk_id");
        record.DefectName = GetStringProperty(root, "defectName", "defect_name", "DefectName", "name", "Name") ?? "Unknown Defect";
        record.DefectTable = GetStringProperty(root, "defectTable", "defect_table", "DefectTable", "table", "tableName") ?? "AOI_DEFECTS";
        record.DefectType = GetStringProperty(root, "defectType", "defect_type", "DefectType", "type") ?? "Optical";
        record.ComponentName = GetStringProperty(root, "componentName", "component_name", "ComponentName", "component", "partName");
        record.Severity = NormalizeSeverity(GetStringProperty(root, "severity", "Severity", "level", "grade"));
        record.Judge = NormalizeJudge(GetStringProperty(root, "judge", "Judge", "decision", "result", "status"));
        record.Reason = GetStringProperty(root, "reason", "Reason", "comment", "defectDescription", "description");
        record.EqpName = GetStringProperty(root, "eqpName", "eqp_name", "EqpName", "equipment");
        record.EmNo = GetStringProperty(root, "emNo", "em_no", "EmNo", "employee", "operator");
        record.SpecName = GetStringProperty(root, "specName", "spec_name", "SpecName");
        record.SpecJudge = GetStringProperty(root, "specJudge", "spec_judge", "SpecJudge");
        record.SpecReason = GetStringProperty(root, "specReason", "spec_reason", "SpecReason");

        // Explicit recognized scope aliases - unknown/missing remain null, never inferred from ambiguous fields
        record.LineCode = GetStringProperty(root, "lineCode", "line_code", "line", "lineId", "line_id", "productionLine", "production_line");
        record.MachineCode = GetStringProperty(root, "machineCode", "machine_code", "machine", "machineId", "machine_id", "equipmentCode", "equipment_code");
        record.StationCode = GetStringProperty(root, "stationCode", "station_code", "station", "stationId", "station_id", "stationName", "station_name");
        record.ClassifierRuleCode = GetStringProperty(root, "classifierRuleCode", "classifier_rule_code", "classifierRule", "classifier_rule", "ruleCode", "rule_code");
        record.ClassifierRuleName = GetStringProperty(root, "classifierRuleName", "classifier_rule_name", "ruleName", "rule_name");
        record.ClassifierRuleVersion = GetStringProperty(root, "classifierRuleVersion", "classifier_rule_version", "ruleVersion", "rule_version", "classifierVersion", "classifier_version");
        record.DefectFamily = GetStringProperty(root, "defectFamily", "defect_family", "family");
        record.DefectClass = GetStringProperty(root, "defectClass", "defect_class", "class");

        if (TryGetDoubleProperty(root, out var cx, "coordinateX", "coordinate_x", "CoordinateX", "x", "X"))
        {
            record.CoordinateX = cx;
        }
        if (TryGetDoubleProperty(root, out var cy, "coordinateY", "coordinate_y", "CoordinateY", "y", "Y"))
        {
            record.CoordinateY = cy;
        }
        if (TryGetDoubleProperty(root, out var conf, "confidenceScore", "confidence_score", "ConfidenceScore", "confidence", "score"))
        {
            record.ConfidenceScore = conf;
        }
        if (TryGetIntProperty(root, out var prio, "priority", "Priority"))
        {
            record.Priority = prio;
        }

        if (TryGetDateTimeProperty(root, out var inspectedAt, "inspectedAtUtc", "inspected_at_utc", "datetime_inspection", "timestamp", "createdAt", "inspectedAt"))
        {
            record.InspectedAtUtc = inspectedAt;
        }

        record.RawFeatureUri = GetStringProperty(root, "rawFeatureUri", "featureUri", "feature_uri", "featurePath");
        record.RawImageUri = GetStringProperty(root, "rawImageUri", "imageUri", "image_uri", "imagePath");

        if (root.TryGetProperty("featureJson", out var fJson) || root.TryGetProperty("feature_json", out fJson) || root.TryGetProperty("features", out fJson))
        {
            record.HasFeatureJson = true;
            var raw = fJson.GetRawText();
            record.FeatureJsonSnippet = raw.Length > maxSnippetLength ? raw[..maxSnippetLength] + "..." : raw;
        }
        else
        {
            record.HasFeatureJson = false;
        }

        // Extract normalized numeric features for analytics
        var features = new List<DefectPreviewFeatureEntity>();
        var seenFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (record.CoordinateX.HasValue)
        {
            AddFeature("CoordinateX", record.CoordinateX.Value, features, seenFeatures);
        }
        if (record.CoordinateY.HasValue)
        {
            AddFeature("CoordinateY", record.CoordinateY.Value, features, seenFeatures);
        }
        AddFeature("ConfidenceScore", record.ConfidenceScore, features, seenFeatures);

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (features.Count >= 64) break;
                if (ReservedRecordProperties.Contains(prop.Name)) continue;

                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var numVal))
                {
                    AddFeature(prop.Name, numVal, features, seenFeatures);
                }
                else if (prop.Value.ValueKind == JsonValueKind.String && double.TryParse(prop.Value.GetString(), out var strVal))
                {
                    AddFeature(prop.Name, strVal, features, seenFeatures);
                }
                else if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ExtractFromContainer(prop.Value, prop.Name, features, seenFeatures, depth: 1, maxDepth: 3, maxFeatures: 64);
                }
            }
        }

        string[] featureContainerNames = ["features", "featureJson", "feature_json", "measurements", "metrics", "customFeatures", "custom_features", "attributes"];
        foreach (var containerName in featureContainerNames)
        {
            if (features.Count >= 64) break;
            if (root.TryGetProperty(containerName, out var container))
            {
                var prefix = containerName.Equals("features", StringComparison.OrdinalIgnoreCase) ||
                             containerName.Equals("featureJson", StringComparison.OrdinalIgnoreCase) ||
                             containerName.Equals("feature_json", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : containerName;
                ExtractFromContainer(container, prefix, features, seenFeatures, depth: 0, maxDepth: 3, maxFeatures: 64);
            }
        }

        foreach (var f in features)
        {
            f.ObservationId = record.ObservationId;
        }

        foreach (var f in features)
        {
            f.ObservationId = record.ObservationId;
        }

        record.ExtractedFeatures = features;
        return record;
    }

    public static IReadOnlyList<(string Name, double Value)> ExtractNumericFeaturesFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var features = new List<DefectPreviewFeatureEntity>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ExtractFromContainer(doc.RootElement, string.Empty, features, seen, depth: 0, maxDepth: 4, maxFeatures: 128);
            return features
                .Select(f => (NormalizeFeatureName(f.FeatureName), f.FeatureValue))
                .Where(f => !string.IsNullOrWhiteSpace(f.Item1))
                .GroupBy(f => f.Item1, StringComparer.OrdinalIgnoreCase)
                .Select(g => (g.Key, g.First().FeatureValue))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static readonly HashSet<string> ReservedRecordProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "defectid", "defect_id", "keyid", "key_id", "defectname", "defect_name",
        "defecttable", "defect_table", "defecttype", "defect_type", "componentname", "component_name",
        "coordinatex", "coordinate_x", "coordinatey", "coordinate_y", "x", "y",
        "severity", "judge", "confidencescore", "confidence_score", "confidence", "score",
        "reason", "eqpname", "eqp_name", "emno", "em_no", "specname", "spec_name",
        "specjudge", "spec_judge", "specreason", "spec_reason", "priority",
        "rawfeatureuri", "rawimageuri", "featureuri", "imageuri", "inspectedatutc",
        "inspected_at_utc", "datetime_inspection", "timestamp", "createdat", "inspectedat",
        "featurejson", "feature_json", "features", "measurements", "metrics",
        "customfeatures", "custom_features", "attributes", "rowindex", "sessionid",
        "observationid", "observation_id", "linecode", "line_code", "line", "machinecode", "machine_code", "machine",
        "stationcode", "station_code", "station", "classifierrulecode", "classifier_rule_code",
        "classifierrulename", "classifier_rule_name", "classifierruleversion", "classifier_rule_version",
        "defectfamily", "defect_family", "defectclass", "defect_class"
    };

    private static void ExtractFromContainer(
        JsonElement element,
        string prefix,
        List<DefectPreviewFeatureEntity> features,
        HashSet<string> seenFeatures,
        int depth,
        int maxDepth,
        int maxFeatures)
    {
        if (depth > maxDepth || features.Count >= maxFeatures) return;

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            if (!string.IsNullOrWhiteSpace(text) && (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('[')))
            {
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    ExtractFromContainer(doc.RootElement, prefix, features, seenFeatures, depth + 1, maxDepth, maxFeatures);
                }
                catch { }
            }
            return;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (features.Count >= maxFeatures) break;

                var name = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var dVal))
                {
                    AddFeature(name, dVal, features, seenFeatures);
                }
                else if (prop.Value.ValueKind == JsonValueKind.String && double.TryParse(prop.Value.GetString(), out var sVal))
                {
                    AddFeature(name, sVal, features, seenFeatures);
                }
                else if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ExtractFromContainer(prop.Value, name, features, seenFeatures, depth + 1, maxDepth, maxFeatures);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var idx = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (features.Count >= maxFeatures) break;

                if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var num))
                {
                    var name = string.IsNullOrEmpty(prefix) ? $"feature[{idx}]" : $"{prefix}[{idx}]";
                    AddFeature(name, num, features, seenFeatures);
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    var featName = GetStringProperty(item, "name", "featureName", "feature_name", "key", "metric", "id");
                    if (!string.IsNullOrWhiteSpace(featName) && TryGetDoubleProperty(item, out var featVal, "value", "featureValue", "feature_value", "val", "score"))
                    {
                        var name = string.IsNullOrEmpty(prefix) ? featName : $"{prefix}.{featName}";
                        AddFeature(name, featVal, features, seenFeatures);
                    }
                    else
                    {
                        ExtractFromContainer(item, prefix, features, seenFeatures, depth + 1, maxDepth, maxFeatures);
                    }
                }
                idx++;
            }
        }
    }

    private static string NormalizeFeatureName(string rawName)
    {
        var name = rawName.Trim();
        foreach (var prefix in new[] { "feature.", "features.", "featureJson.", "feature_json.", "customFeature.", "customFeatures.", "custom_feature.", "custom_features." })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
                break;
            }
        }
        return name.Trim();
    }

    private static void AddFeature(string rawName, double value, List<DefectPreviewFeatureEntity> features, HashSet<string> seenFeatures)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        var cleanName = rawName.Trim();
        if (cleanName.Length > 64) cleanName = cleanName[..64];
        if (cleanName.Length == 0) return;

        if (seenFeatures.Add(cleanName))
        {
            features.Add(new DefectPreviewFeatureEntity
            {
                FeatureName = cleanName,
                FeatureValue = value
            });
        }
    }

    private static string? GetStringProperty(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                {
                    var val = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                }
                else if (prop.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    return prop.GetRawText();
                }
            }
        }

        // Case-insensitive fallback
        foreach (var prop in element.EnumerateObject())
        {
            if (names.Any(n => string.Equals(n, prop.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var val = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                }
                else if (prop.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    return prop.Value.GetRawText();
                }
            }
        }
        return null;
    }

    private static bool TryGetDoubleProperty(JsonElement element, out double value, params string[] names)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Object) return false;

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out value))
                {
                    return true;
                }
                if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out value))
                {
                    return true;
                }
            }
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (names.Any(n => string.Equals(n, prop.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out value))
                {
                    return true;
                }
                if (prop.Value.ValueKind == JsonValueKind.String && double.TryParse(prop.Value.GetString(), out value))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool TryGetIntProperty(JsonElement element, out int value, params string[] names)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Object) return false;

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
                {
                    return true;
                }
                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value))
                {
                    return true;
                }
            }
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (names.Any(n => string.Equals(n, prop.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out value))
                {
                    return true;
                }
                if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out value))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool TryGetDateTimeProperty(JsonElement element, out DateTimeOffset value, params string[] names)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(prop.GetString(), out value))
                {
                    return true;
                }
            }
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (names.Any(n => string.Equals(n, prop.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (prop.Value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(prop.Value.GetString(), out value))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool TryGetGuidProperty(JsonElement element, out Guid value, params string[] names)
    {
        value = Guid.Empty;
        if (element.ValueKind != JsonValueKind.Object) return false;

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String && Guid.TryParse(prop.GetString(), out value))
                {
                    return true;
                }
            }
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (names.Any(n => string.Equals(n, prop.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (prop.Value.ValueKind == JsonValueKind.String && Guid.TryParse(prop.Value.GetString(), out value))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static string NormalizeJudge(string? judge)
    {
        if (string.IsNullOrWhiteSpace(judge)) return "NG";
        var trimmed = judge.Trim().ToUpperInvariant();
        if (trimmed is "OK" or "PASS" or "GOOD") return "OK";
        if (trimmed is "NG" or "FAIL" or "DEFECT" or "REJECT") return "NG";
        if (trimmed is "OVERKILL" or "FALSE_ALARM") return "Overkill";
        if (trimmed is "LEAKAGE" or "ESCAPE") return "Leakage";
        return trimmed;
    }

    private static string NormalizeSeverity(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity)) return "Minor";
        var trimmed = severity.Trim().ToUpperInvariant();
        if (trimmed is "CRITICAL" or "FATAL") return "Critical";
        if (trimmed is "MAJOR" or "HIGH") return "Major";
        if (trimmed is "MINOR" or "LOW" or "MED" or "MEDIUM") return "Minor";
        return "Minor";
    }
}
