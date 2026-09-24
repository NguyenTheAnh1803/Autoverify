using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AutoVerify.Api.Services;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class DefectStreamParserTests
{
    [Fact]
    public void CanonicalAndLegacyNormalization_ResolvesCorrectly()
    {
        // Canonical formats
        Assert.True(DefectDataFormats.TryNormalize("auto", out var auto));
        Assert.Equal(DefectDataFormats.Auto, auto);

        Assert.True(DefectDataFormats.TryNormalize("json", out var json));
        Assert.Equal(DefectDataFormats.Json, json);

        Assert.True(DefectDataFormats.TryNormalize("jsonl", out var jsonl));
        Assert.Equal(DefectDataFormats.Jsonl, jsonl);

        Assert.True(DefectDataFormats.TryNormalize("gzip", out var gzip));
        Assert.Equal(DefectDataFormats.Gzip, gzip);

        Assert.True(DefectDataFormats.TryNormalize("zip", out var zip));
        Assert.Equal(DefectDataFormats.Zip, zip);

        // Legacy aliases
        Assert.True(DefectDataFormats.TryNormalize("ndjson", out var ndjson));
        Assert.Equal(DefectDataFormats.Jsonl, ndjson);

        Assert.True(DefectDataFormats.TryNormalize("jsonlines", out var jsonlines));
        Assert.Equal(DefectDataFormats.Jsonl, jsonlines);

        Assert.True(DefectDataFormats.TryNormalize("defect_pack_gz", out var defectPackGz));
        Assert.Equal(DefectDataFormats.Gzip, defectPackGz);

        Assert.True(DefectDataFormats.TryNormalize("DefectPackJson", out var defectPackJson));
        Assert.Equal(DefectDataFormats.Gzip, defectPackJson);

        Assert.True(DefectDataFormats.TryNormalize("bundle_zip", out var bundleZip));
        Assert.Equal(DefectDataFormats.Zip, bundleZip);

        Assert.True(DefectDataFormats.TryNormalize("storagefile", out var storageFile));
        Assert.Equal(DefectDataFormats.Auto, storageFile);

        // Case insensitivity and whitespace handling
        Assert.True(DefectDataFormats.TryNormalize("  JSONL  ", out var wsJsonl));
        Assert.Equal(DefectDataFormats.Jsonl, wsJsonl);

        // Invalid format
        Assert.False(DefectDataFormats.TryNormalize("unknown_xyz", out var invalid));
        Assert.Equal(string.Empty, invalid);
    }

    [Fact]
    public async Task PlainJsonArray_ParsesSuccessfully()
    {
        var json = """
        [
            { "id": "DEF-001", "defectName": "Bridge", "judge": "NG", "severity": "MAJOR" },
            { "id": "DEF-002", "defectName": "Void", "judge": "OK", "severity": "MINOR" }
        ]
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

        await foreach (var record in DefectStreamParser.ParseDefectsAsync(ms, "test://plain_array.json", "json"))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("DEF-001", records[0].DefectId);
        Assert.Equal("Bridge", records[0].DefectName);
        Assert.Equal("NG", records[0].Judge);
        Assert.Equal("DEF-002", records[1].DefectId);
        Assert.Equal("OK", records[1].Judge);
    }

    [Fact]
    public async Task WrapperJsonObject_ParsesSuccessfully()
    {
        var json = """
        {
            "productId": "PROD-100",
            "totalDefects": 2,
            "defects": [
                { "defectId": "D-1", "defectName": "Scratch", "judge": "NG" },
                { "defectId": "D-2", "defectName": "SolderBall", "judge": "NG" }
            ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

        await foreach (var record in DefectStreamParser.ParseDefectsAsync(ms, "test://wrapper.json", "auto"))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("D-1", records[0].DefectId);
        Assert.Equal("Scratch", records[0].DefectName);
        Assert.Equal("D-2", records[1].DefectId);
        Assert.Equal("SolderBall", records[1].DefectName);
    }

    [Fact]
    public async Task SingleJsonObject_ParsesSuccessfully()
    {
        var json = """
        {
            "id": "SINGLE-001",
            "defectName": "MisalignedComponent",
            "judge": "NG",
            "severity": "CRITICAL",
            "componentName": "U2",
            "coordinateX": 12.34,
            "coordinateY": 56.78
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

        await foreach (var record in DefectStreamParser.ParseDefectsAsync(ms, "test://single.json", "auto"))
        {
            records.Add(record);
        }

        Assert.Single(records);
        Assert.Equal("SINGLE-001", records[0].DefectId);
        Assert.Equal("MisalignedComponent", records[0].DefectName);
        Assert.Equal("NG", records[0].Judge);
        Assert.Equal("Critical", records[0].Severity, ignoreCase: true);
        Assert.Equal("U2", records[0].ComponentName);
        Assert.Equal(12.34, records[0].CoordinateX);
        Assert.Equal(56.78, records[0].CoordinateY);
    }

    [Fact]
    public async Task JsonlWithBlankLines_ParsesSuccessfully()
    {
        var jsonl = """

        {"id": "L-1", "defectName": "DefectOne", "judge": "NG"}

        
        {"id": "L-2", "defectName": "DefectTwo", "judge": "OK"}
        
        {"id": "L-3", "defectName": "DefectThree", "judge": "NG"}

        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

        await foreach (var record in DefectStreamParser.ParseDefectsAsync(ms, "test://feed.jsonl", "auto"))
        {
            records.Add(record);
        }

        Assert.Equal(3, records.Count);
        Assert.Equal("L-1", records[0].DefectId);
        Assert.Equal("L-2", records[1].DefectId);
        Assert.Equal("L-3", records[2].DefectId);
    }

    [Fact]
    public async Task GzipMagicDetection_WithoutExtension_DetectsAndParses()
    {
        var payload = """
        [
            {"id": "GZ-1", "defectName": "GzipDefect1", "judge": "NG"},
            {"id": "GZ-2", "defectName": "GzipDefect2", "judge": "OK"}
        ]
        """;

        using var uncompressedMs = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        using var gzipMs = new MemoryStream();
        using (var gz = new GZipStream(gzipMs, CompressionLevel.Optimal, leaveOpen: true))
        {
            await uncompressedMs.CopyToAsync(gz);
        }

        var compressedBytes = gzipMs.ToArray();
        Assert.Equal(0x1F, compressedBytes[0]);
        Assert.Equal(0x8B, compressedBytes[1]);

        // Extensionless URI
        using var streamToParse = new MemoryStream(compressedBytes);
        var resolvedFormat = string.Empty;
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

        var metadata = new StreamMetadata(SourceUri: "http://storage.internal/blobs/abc123signed_no_ext", FormatHint: "auto");
        await foreach (var record in DefectStreamParser.ParseDefectsAsync(
            streamToParse,
            metadata,
            onFormatResolved: fmt => resolvedFormat = fmt))
        {
            records.Add(record);
        }

        Assert.Equal(DefectDataFormats.Gzip, resolvedFormat);
        Assert.Equal(2, records.Count);
        Assert.Equal("GZ-1", records[0].DefectId);
        Assert.Equal("GZ-2", records[1].DefectId);
    }

    [Fact]
    public async Task ZipMagicDetection_WithoutExtension_DetectsAndParses()
    {
        using var zipMs = new MemoryStream();
        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("defects.json");
            await using var es = entry.Open();
            var entryData = Encoding.UTF8.GetBytes("""[{"id": "ZIP-01", "defectName": "SolderCrack", "judge": "NG"}]""");
            await es.WriteAsync(entryData);
        }

        var zipBytes = zipMs.ToArray();
        Assert.Equal(0x50, zipBytes[0]); // 'P'
        Assert.Equal(0x4B, zipBytes[1]); // 'K'

        using var streamToParse = new MemoryStream(zipBytes);
        var resolvedFormat = string.Empty;
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

        var metadata = new StreamMetadata(SourceUri: "http://seaweedfs.local/filer/raw_object_hash_only", FormatHint: null);
        await foreach (var record in DefectStreamParser.ParseDefectsAsync(
            streamToParse,
            metadata,
            onFormatResolved: fmt => resolvedFormat = fmt))
        {
            records.Add(record);
        }

        Assert.Equal(DefectDataFormats.Zip, resolvedFormat);
        Assert.Single(records);
        Assert.Equal("ZIP-01", records[0].DefectId);
        Assert.Equal("SolderCrack", records[0].DefectName);
    }

    [Fact]
    public async Task ZipWithManifestAndSeparateDataEntry_PrioritizesDataEntry()
    {
        // Bundle has manifest.json pointing to data, plus separate defects.jsonl
        using var zipMs = new MemoryStream();
        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            await using (var es = manifestEntry.Open())
            {
                var manifestBytes = Encoding.UTF8.GetBytes("""
                {
                    "batchId": "BATCH-1",
                    "files": ["defects_actual.jsonl"]
                }
                """);
                await es.WriteAsync(manifestBytes);
            }

            var dataEntry = archive.CreateEntry("defects_actual.jsonl");
            await using (var es = dataEntry.Open())
            {
                var dataBytes = Encoding.UTF8.GetBytes("""
                {"id": "ACT-1", "defectName": "ActualDefect1", "judge": "NG"}
                {"id": "ACT-2", "defectName": "ActualDefect2", "judge": "NG"}
                """);
                await es.WriteAsync(dataBytes);
            }

            var featureEntry = archive.CreateEntry("ACT-1_feature.json");
            await using (var es = featureEntry.Open())
            {
                await es.WriteAsync(Encoding.UTF8.GetBytes("""{"area": 42}"""));
            }
        }

        zipMs.Position = 0;
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

        await foreach (var record in DefectStreamParser.ParseDefectsAsync(zipMs, "test://bundle_with_manifest.zip", "zip"))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("ACT-1", records[0].DefectId);
        Assert.Equal("ActualDefect1", records[0].DefectName);
        Assert.Equal("ACT-2", records[1].DefectId);
    }

    [Fact]
    public async Task ZipWithManifestEmbeddedDefects_ParsesManifestWhenNoOtherDataEntry()
    {
        // Matches SeaweedFsStorageManager.UploadProductDefectBundleAsync
        using var zipMs = new MemoryStream();
        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            await using (var es = manifestEntry.Open())
            {
                var manifestBytes = Encoding.UTF8.GetBytes("""
                {
                    "KeyId": "PROD-888",
                    "DefectsCount": 2,
                    "Defects": [
                        { "Id": "DEF-888-1", "DefectName": "ShortCircuit", "Judge": "NG", "Severity": "Critical" },
                        { "Id": "DEF-888-2", "DefectName": "MissingPart", "Judge": "NG", "Severity": "Major" }
                    ]
                }
                """);
                await es.WriteAsync(manifestBytes);
            }

            // Also per-defect feature entries
            var feat1 = archive.CreateEntry("DEF-888-1_feature.json");
            await using (var es = feat1.Open()) await es.WriteAsync(Encoding.UTF8.GetBytes("""{"area": 100}"""));

            var feat2 = archive.CreateEntry("DEF-888-2_feature.json");
            await using (var es = feat2.Open()) await es.WriteAsync(Encoding.UTF8.GetBytes("""{"area": 200}"""));
        }

        zipMs.Position = 0;
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

        await foreach (var record in DefectStreamParser.ParseDefectsAsync(zipMs, "test://product_bundle.zip", "zip"))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("DEF-888-1", records[0].DefectId);
        Assert.Equal("ShortCircuit", records[0].DefectName);
        Assert.Equal("DEF-888-2", records[1].DefectId);
        Assert.Equal("MissingPart", records[1].DefectName);
    }

    [Fact]
    public async Task ZipMultipleDataEntries_MergesRecordsSequentially()
    {
        using var zipMs = new MemoryStream();
        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var part1 = archive.CreateEntry("part1_defects.jsonl");
            await using (var es = part1.Open())
            {
                await es.WriteAsync(Encoding.UTF8.GetBytes("""
                {"id": "P1-1", "defectName": "D1"}
                {"id": "P1-2", "defectName": "D2"}
                """));
            }

            var part2 = archive.CreateEntry("part2_defects.jsonl");
            await using (var es = part2.Open())
            {
                await es.WriteAsync(Encoding.UTF8.GetBytes("""
                {"id": "P2-1", "defectName": "D3"}
                {"id": "P2-2", "defectName": "D4"}
                """));
            }
        }

        zipMs.Position = 0;
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

        await foreach (var record in DefectStreamParser.ParseDefectsAsync(zipMs, "test://multi_part.zip", "zip"))
        {
            records.Add(record);
        }

        Assert.Equal(4, records.Count);
        Assert.Equal("P1-1", records[0].DefectId);
        Assert.Equal("P1-2", records[1].DefectId);
        Assert.Equal("P2-1", records[2].DefectId);
        Assert.Equal("P2-2", records[3].DefectId);
    }

    [Fact]
    public async Task MalformedInput_ThrowsDefectParseExceptionWithDiagnostics()
    {
        var malformedJson = """[ {"id": "OK-1"}, {"id": "BROKEN", "defectName": """;
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(malformedJson));

        var ex = await Assert.ThrowsAsync<DefectParseException>(async () =>
        {
            await foreach (var _ in DefectStreamParser.ParseDefectsAsync(ms, "test://malformed.json", "json"))
            {
            }
        });

        Assert.Contains("ParseJson", ex.Stage);
        Assert.Equal(DefectDataFormats.Json, ex.Format);
        Assert.Contains("Invalid JSON syntax", ex.Message);
    }

    [Fact]
    public async Task MaxRowsLimitAcrossEntries_Respected()
    {
        var json = """
        [
            {"id": "R-1"}, {"id": "R-2"}, {"id": "R-3"},
            {"id": "R-4"}, {"id": "R-5"}, {"id": "R-6"}
        ]
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

        var metadata = new StreamMetadata(SourceUri: "test://limit.json", FormatHint: "json");
        await foreach (var record in DefectStreamParser.ParseDefectsAsync(ms, metadata, maxRowsLimit: 3))
        {
            records.Add(record);
        }

        Assert.Equal(3, records.Count);
        Assert.Equal("R-1", records[0].DefectId);
        Assert.Equal("R-2", records[1].DefectId);
        Assert.Equal("R-3", records[2].DefectId);
    }

    [Fact]
    public async Task ZipSafeguard_ExceedingMaxEntries_Rejects()
    {
        using var zipMs = new MemoryStream();
        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < 15; i++)
            {
                var entry = archive.CreateEntry($"entry_{i}.json");
                await using var es = entry.Open();
                await es.WriteAsync(Encoding.UTF8.GetBytes("{}"));
            }
        }

        zipMs.Position = 0;
        var safeguards = new ZipSafeguardOptions { MaxEntries = 10 }; // Limit to 10

        var metadata = new StreamMetadata(SourceUri: "test://bomb_entries.zip", FormatHint: "zip");
        var ex = await Assert.ThrowsAsync<DefectParseException>(async () =>
        {
            await foreach (var _ in DefectStreamParser.ParseDefectsAsync(zipMs, metadata, zipSafeguards: safeguards))
            {
            }
        });

        Assert.Contains("exceeds safeguard limit of 10", ex.Message);
    }

    [Fact]
    public async Task ZipSafeguard_ZipSlip_Rejects()
    {
        using var zipMs = new MemoryStream();
        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var evilEntry = archive.CreateEntry("../../etc/passwd");
            await using var es = evilEntry.Open();
            await es.WriteAsync(Encoding.UTF8.GetBytes("root:x:0:0"));
        }

        zipMs.Position = 0;
        var metadata = new StreamMetadata(SourceUri: "test://zip_slip.zip", FormatHint: "zip");

        var ex = await Assert.ThrowsAsync<DefectParseException>(async () =>
        {
            await foreach (var _ in DefectStreamParser.ParseDefectsAsync(zipMs, metadata))
            {
            }
        });

        Assert.Contains("ZipSlip", ex.Message);
    }

    [Fact]
    public void ConvertJsonElementToRecord_ExtractsNestedNumericFeatures()
    {
        string json = """
        {
            "defect_id": "DEF-001",
            "judge": "NG",
            "severity": "CRITICAL",
            "component_name": "R101",
            "eqp_name": "AOI-01",
            "metrics": {
                "area": 45.8,
                "aspect_ratio": 1.42,
                "sub_metrics": {
                    "contrast": 88.5,
                    "invalid_nan": null
                }
            },
            "array_features": [10.5, 20.2]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var record = DefectStreamParser.ConvertJsonElementToRecord(doc.RootElement, "test://defect_001.json");

        Assert.NotNull(record);
        Assert.Equal("DEF-001", record.DefectId);
        Assert.Equal("NG", record.Judge);
        Assert.Equal("Critical", record.Severity, ignoreCase: true);
        Assert.Equal("R101", record.ComponentName);
        Assert.Equal("AOI-01", record.EqpName);

        // Verification of extracted numeric features
        Assert.NotEmpty(record.ExtractedFeatures);
        var areaFeature = record.ExtractedFeatures.FirstOrDefault(f => f.FeatureName == "metrics.area");
        var contrastFeature = record.ExtractedFeatures.FirstOrDefault(f => f.FeatureName == "metrics.sub_metrics.contrast");
        var arrFeature = record.ExtractedFeatures.FirstOrDefault(f => f.FeatureName == "array_features[0]");

        Assert.NotNull(areaFeature);
        Assert.Equal(45.8, areaFeature.FeatureValue);

        Assert.NotNull(contrastFeature);
        Assert.Equal(88.5, contrastFeature.FeatureValue);

        Assert.NotNull(arrFeature);
        Assert.Equal(10.5, arrFeature.FeatureValue);
    }

    [Fact]
    public void DefectFormatResolver_RespectsMetadataPrecedence()
    {
        // 1. ContentEncoding gzip takes precedence over non-magic stream
        var emptyBytes = "   "u8.ToArray();
        var metaEncodingGz = new StreamMetadata(SourceUri: "http://storage/blob", ContentEncoding: "gzip");
        Assert.Equal(DefectDataFormats.Gzip, DefectFormatResolver.ResolveFormat(emptyBytes, metaEncodingGz));

        // 2. ContentType application/zip takes precedence over non-magic stream
        var metaZip = new StreamMetadata(SourceUri: "http://storage/blob", ContentType: "application/zip");
        Assert.Equal(DefectDataFormats.Zip, DefectFormatResolver.ResolveFormat(emptyBytes, metaZip));

        // 3. ContentType ndjson
        var metaNdjson = new StreamMetadata(SourceUri: "http://storage/blob", ContentType: "application/x-ndjson");
        Assert.Equal(DefectDataFormats.Jsonl, DefectFormatResolver.ResolveFormat(emptyBytes, metaNdjson));

        // 4. Magic bytes take precedence over incorrect ContentType
        var gzipMagic = new byte[] { 0x1F, 0x8B, 0x08 };
        var wrongMeta = new StreamMetadata(SourceUri: "test.json", ContentType: "application/json");
        Assert.Equal(DefectDataFormats.Gzip, DefectFormatResolver.ResolveFormat(gzipMagic, wrongMeta));

        // 5. ZIP magic bytes take precedence over incorrect ContentType
        var zipMagic = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        Assert.Equal(DefectDataFormats.Zip, DefectFormatResolver.ResolveFormat(zipMagic, wrongMeta));
    }

    [Fact]
    public async Task ZipSafeguard_ExceedingTotalUncompressedBytes_Rejects()
    {
        using var zipMs = new MemoryStream();
        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry1 = archive.CreateEntry("part1.json");
            await using (var es = entry1.Open())
            {
                await es.WriteAsync(Encoding.UTF8.GetBytes(new string('A', 5000)));
            }

            var entry2 = archive.CreateEntry("part2.json");
            await using (var es = entry2.Open())
            {
                await es.WriteAsync(Encoding.UTF8.GetBytes(new string('B', 6000)));
            }
        }

        zipMs.Position = 0;
        var safeguards = new ZipSafeguardOptions { MaxTotalUncompressedBytes = 8000 }; // total is 11,000

        var metadata = new StreamMetadata(SourceUri: "test://total_limit.zip", FormatHint: "zip");
        var ex = await Assert.ThrowsAsync<DefectParseException>(async () =>
        {
            await foreach (var _ in DefectStreamParser.ParseDefectsAsync(zipMs, metadata, zipSafeguards: safeguards))
            {
            }
        });

        Assert.Contains("total uncompressed size exceeds safeguard limit", ex.Message);
    }

    [Fact]
    public async Task ZipSafeguard_ExceedingPerEntryBytes_Rejects()
    {
        using var zipMs = new MemoryStream();
        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("huge.json");
            await using (var es = entry.Open())
            {
                await es.WriteAsync(Encoding.UTF8.GetBytes(new string('Z', 10000)));
            }
        }

        zipMs.Position = 0;
        var safeguards = new ZipSafeguardOptions { MaxEntryUncompressedBytes = 5000 }; // entry is 10,000

        var metadata = new StreamMetadata(SourceUri: "test://entry_limit.zip", FormatHint: "zip");
        var ex = await Assert.ThrowsAsync<DefectParseException>(async () =>
        {
            await foreach (var _ in DefectStreamParser.ParseDefectsAsync(zipMs, metadata, zipSafeguards: safeguards))
            {
            }
        });

        Assert.Contains("uncompressed size", ex.Message);
        Assert.Contains("exceeds safeguard limit", ex.Message);
    }

    [Fact]
    public void DefectPreviewOptions_MaxRowsLimit_DefaultsToFiftyThousand()
    {
        var options = new DefectPreviewOptions();
        Assert.Equal(50000, options.MaxRowsLimit);
    }

    [Theory]
    [InlineData(1, 50, 1)]
    [InlineData(50, 50, 1)]
    [InlineData(51, 50, 2)]
    [InlineData(100, 50, 2)]
    [InlineData(101, 50, 3)]
    [InlineData(25, 25, 1)]
    [InlineData(26, 25, 2)]
    public void RowIndexPageFormula_MapsToExpectedOneBasedPage(int rowIndex, int pageSize, int expectedPage)
    {
        // Matches frontend Math.floor((rowIndex - 1) / pageSize) + 1
        var page = (int)Math.Floor((double)(rowIndex - 1) / pageSize) + 1;
        Assert.Equal(expectedPage, page);
    }

    [Fact]
    public async Task ParseDefectsAsync_WithMaxRowsLimit_HaltsAtLimit()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[");
        for (int i = 1; i <= 10; i++)
        {
            sb.Append($"{{\"id\":\"DEF-{i:D3}\",\"defectName\":\"Defect_{i}\",\"judge\":\"NG\"}}");
            if (i < 10) sb.AppendLine(",");
            else sb.AppendLine();
        }
        sb.AppendLine("]");

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        var metadata = new StreamMetadata(SourceUri: "test://limit.json", FormatHint: "json");

        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();
        await foreach (var record in DefectStreamParser.ParseDefectsAsync(ms, metadata, maxRowsLimit: 4))
        {
            records.Add(record);
        }

        Assert.Equal(4, records.Count);
        Assert.Equal("DEF-001", records[0].DefectId);
        Assert.Equal("DEF-004", records[3].DefectId);
    }

    [Fact]
    public async Task LargeThirdRecordCrossingBufferBoundary_ReproducesIssue()
    {
        // Construct row 1 and row 2 taking ~23,277 bytes, followed by row 3
        var row1Payload = new string('x', 11000);
        var row2Payload = new string('y', 12000);
        var row3Payload = new string('z', 50000);

        var json = $$"""
        [
            {"id": "DEF-001", "defectName": "Defect1", "reason": "{{row1Payload}}"},
            {"id": "DEF-002", "defectName": "Defect2", "reason": "{{row2Payload}}"},
            {"id": "DEF-003", "defectName": "Defect3", "reason": "{{row3Payload}}"}
        ]
        """;

        // Feed in 16KB chunks to force buffer boundaries
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        using var chunked = new ChunkedReadStream(ms, chunkSize: 16 * 1024);

        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();
        await foreach (var record in DefectStreamParser.ParseDefectsAsync(chunked, "test://large_third.json", "json"))
        {
            records.Add(record);
        }

        Assert.Equal(3, records.Count);
    }

    [Fact]
    public async Task LargeThirdRecordCrossingBufferBoundary_WithGzip_ParsesSuccessfully()
    {
        var row1Payload = new string('a', 11000);
        var row2Payload = new string('b', 12000);
        var row3Payload = new string('c', 50000);

        var json = $$"""
        [
            {"id": "DEF-001", "defectName": "Defect1", "reason": "{{row1Payload}}"},
            {"id": "DEF-002", "defectName": "Defect2", "reason": "{{row2Payload}}"},
            {"id": "DEF-003", "defectName": "Defect3", "reason": "{{row3Payload}}"}
        ]
        """;

        using var rawMs = new MemoryStream();
        using (var gzip = new GZipStream(rawMs, CompressionLevel.Fastest, leaveOpen: true))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(json));
        }
        rawMs.Position = 0;

        using var chunked = new ChunkedReadStream(rawMs, chunkSize: 8 * 1024);
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();
        await foreach (var record in DefectStreamParser.ParseDefectsAsync(chunked, "728461_defects.json.gz", "json"))
        {
            records.Add(record);
        }

        Assert.Equal(3, records.Count);
        Assert.Equal("DEF-001", records[0].DefectId);
        Assert.Equal("DEF-002", records[1].DefectId);
        Assert.Equal("DEF-003", records[2].DefectId);
    }

    [Fact]
    public async Task ValidNestedObjectsAndArraysWithCommasAndEscapedQuotes_AcrossBuffer_ParsesSuccessfully()
    {
        var complexString = "Complex text with \\\"escaped quotes\\\", commas, and brackets: [1, 2, {\\\"k\\\": \\\"v\\\"}]. " + new string('q', 20000);
        var json = $$"""
        [
            {
                "id": "DEF-COMPLEX-1",
                "defectName": "Complex 1",
                "nested": {
                    "field1": "{{complexString}}",
                    "list": ["item, 1", "item, 2", {"inner": "val, with, comma"}]
                }
            },
            {
                "id": "DEF-COMPLEX-2",
                "defectName": "Complex 2",
                "features": {
                    "f1": 12.34,
                    "f2": 56.78
                }
            }
        ]
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        using var chunked = new ChunkedReadStream(ms, chunkSize: 4 * 1024);

        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();
        await foreach (var record in DefectStreamParser.ParseDefectsAsync(chunked, "test://nested.json", "json"))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("DEF-COMPLEX-1", records[0].DefectId);
        Assert.Equal("DEF-COMPLEX-2", records[1].DefectId);
    }

    [Fact]
    public async Task RepeatedComma_NearByte23277_StrictFailsWithBoundedDiagnostic_AndRecoversWhenEnabled()
    {
        // Row 1 and 2 take ~23,200 bytes on a single line (LineNumber: 0), row 3 has repeated comma `,,`
        var pad1 = new string('x', 11500);
        var pad2 = new string('y', 11500);

        var corruptedJson = $"[{{\"id\":\"DEF-001\",\"pad\":\"{pad1}\"}},{{\"id\":\"DEF-002\",\"pad\":\"{pad2}\"}},{{\"id\":\"DEF-003\",,\"defectName\":\"Defect3\"}}]";

        var bytes = Encoding.UTF8.GetBytes(corruptedJson);

        // 1. Strict mode (enableRecovery = false)
        using (var msStrict = new MemoryStream(bytes))
        {
            var ex = await Assert.ThrowsAsync<DefectParseException>(async () =>
            {
                await foreach (var _ in DefectStreamParser.ParseDefectsAsync(
                    msStrict,
                    new StreamMetadata("test://corrupt.json", "json"),
                    enableRecovery: false))
                {
                }
            });

            Assert.Equal(3, ex.RowIndex);
            Assert.True(ex.ByteOffset > 23000, $"Expected byte offset > 23000, got {ex.ByteOffset}");
            Assert.Contains("Repeated comma", ex.LikelyCause);
            Assert.NotEmpty(ex.ErrorContext ?? string.Empty);
            Assert.NotEmpty(ex.ActionMessage ?? string.Empty);
        }

        // 2. Recovery mode (enableRecovery = true)
        using (var msRecover = new MemoryStream(bytes))
        {
            string? warning = null;
            var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

            await foreach (var record in DefectStreamParser.ParseDefectsAsync(
                msRecover,
                new StreamMetadata("test://corrupt.json", "json"),
                onWarning: w => warning = w,
                enableRecovery: true))
            {
                records.Add(record);
            }

            Assert.Equal(3, records.Count);
            Assert.Equal("DEF-003", records[2].DefectId);
            Assert.NotNull(warning);
            Assert.Contains("normalized 1 misplaced/repeated comma delimiter(s)", warning);
        }
    }

    [Fact]
    public async Task StrictTrailingComma_NormalizesAndRecoversWithWarning()
    {
        var jsonWithTrailingComma = """
        [
            {"id": "DEF-001", "name": "Defect 1",},
            {"id": "DEF-002", "name": "Defect 2",}
        ]
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonWithTrailingComma));
        string? warning = null;
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

        await foreach (var record in DefectStreamParser.ParseDefectsAsync(
            ms,
            new StreamMetadata("test://trailing.json", "json"),
            onWarning: w => warning = w,
            enableRecovery: true))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.NotNull(warning);
        Assert.Contains("normalized 2 misplaced/repeated comma delimiter(s)", warning);
    }

    [Fact]
    public async Task UnambiguousJsonlMislabelledAsJson_RecoversDeterministically()
    {
        var jsonlContent = """
        {"id": "DEF-001", "defectName": "Defect 1"}
        {"id": "DEF-002", "defectName": "Defect 2"}
        {"id": "DEF-003", "defectName": "Defect 3"}
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonlContent));
        string? warning = null;
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

        await foreach (var record in DefectStreamParser.ParseDefectsAsync(
            ms,
            new StreamMetadata(SourceUri: "test://data.json", FormatHint: "json"), // explicitly mislabelled as json hint instead of jsonl
            onWarning: w => warning = w,
            enableRecovery: true))
        {
            records.Add(record);
        }

        Assert.Equal(3, records.Count);
        Assert.Equal("DEF-001", records[0].DefectId);
        Assert.Equal("DEF-003", records[2].DefectId);
        Assert.NotNull(warning);
        Assert.Contains("Detected line-delimited JSON", warning);
    }

    [Fact]
    public async Task GzipWrapped_RepeatedComma_RecoversWithWarning()
    {
        var corruptedJson = """
        [
            {"id": "DEF-001", "defectName": "Defect 1"},
            {"id": "DEF-002",,"defectName": "Defect 2"}
        ]
        """;

        using var rawMs = new MemoryStream();
        using (var gzip = new GZipStream(rawMs, CompressionLevel.Fastest, leaveOpen: true))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(corruptedJson));
        }
        rawMs.Position = 0;

        string? warning = null;
        var records = new List<AutoVerify.Api.Persistence.Entities.DefectPreviewRecordEntity>();

        await foreach (var record in DefectStreamParser.ParseDefectsAsync(
            rawMs,
            new StreamMetadata("corrupted_defects.json.gz", "json"),
            onWarning: w => warning = w,
            enableRecovery: true))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.NotNull(warning);
        Assert.Contains("normalized 1 misplaced/repeated comma delimiter(s)", warning);
    }

    private sealed class ChunkedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _chunkSize;

        public ChunkedReadStream(Stream inner, int chunkSize)
        {
            _inner = inner;
            _chunkSize = chunkSize;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, _chunkSize));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var toRead = Math.Min(buffer.Length, _chunkSize);
            return await _inner.ReadAsync(buffer.Slice(0, toRead), cancellationToken);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
