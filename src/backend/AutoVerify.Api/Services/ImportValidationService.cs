using System.Security.Cryptography;
using System.Text;

namespace AutoVerify.Api.Services;

public interface IImportValidationService
{
    ImportPreviewResponse ValidateAndPreview(ImportPreviewRequest request);
    string NormalizeId(string value);
    string NormalizeConfigKey(string value);
}

public sealed class ImportValidationService : IImportValidationService
{
    public ImportPreviewResponse ValidateAndPreview(ImportPreviewRequest request)
    {
        var category = NormalizeClassification(request.VerificationCategory, "Leakage");
        var machine = NormalizeConfigKey(string.IsNullOrWhiteSpace(request.MachineCode) ? "UNASSIGNED" : request.MachineCode);
        var lineTarget = string.IsNullOrWhiteSpace(request.LineCode) || request.LineCode.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? null
            : NormalizeConfigKey(request.LineCode);

        var hasLineWarning = lineTarget is null;

        var rawLines = request.RawText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var accepted = new List<ImportAcceptedRow>();
        var rejected = new List<ImportRejectedRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateCount = 0;

        for (var i = 0; i < rawLines.Length; i++)
        {
            var rawLine = rawLines[i];
            var parts = rawLine.Split(new[] { ',', '\t', ';' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                rejected.Add(new ImportRejectedRow(i + 1, rawLine, "Empty line after normalization."));
                continue;
            }

            var entityId = NormalizeId(parts[0]);
            if (string.IsNullOrWhiteSpace(entityId))
            {
                rejected.Add(new ImportRejectedRow(i + 1, rawLine, "Key ID is empty."));
                continue;
            }

            if (entityId.Length > 64)
            {
                rejected.Add(new ImportRejectedRow(i + 1, rawLine, "Key ID exceeds maximum length of 64 characters."));
                continue;
            }

            string? rowLine = lineTarget;
            int? rowMappingInt = null;
            string? rowMapping = request.MappingName;
            string? warning = null;

            if (category == "Leakage")
            {
                // Concept: ID, Line, Mapping (int)
                if (parts.Length >= 2)
                {
                    rowLine = NormalizeConfigKey(parts[1]);
                }
                if (parts.Length >= 3)
                {
                    if (int.TryParse(parts[2], out var parsedInt))
                    {
                        rowMappingInt = parsedInt;
                        rowMapping = parsedInt.ToString();
                    }
                    else
                    {
                        rejected.Add(new ImportRejectedRow(i + 1, rawLine, $"Mapping column for Leakage must be an integer. Received value: '{parts[2]}'."));
                        continue;
                    }
                }
                else
                {
                    rowMappingInt = 1;
                    rowMapping = "1";
                }
            }
            else
            {
                // Concept Overkill: ID, Line
                if (parts.Length >= 2)
                {
                    rowLine = NormalizeConfigKey(parts[1]);
                }
                if (parts.Length >= 3)
                {
                    rowMapping = parts[2];
                    if (int.TryParse(parts[2], out var parsedInt))
                    {
                        rowMappingInt = parsedInt;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(rowLine) || rowLine.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                rowLine = "ALL";
                warning = "No specific line provided for this row (system will scan entire machine - not recommended).";
            }

            if (!seen.Add(entityId))
            {
                duplicateCount++;
                rejected.Add(new ImportRejectedRow(i + 1, rawLine, "Duplicate Key ID in list."));
                continue;
            }

            accepted.Add(new ImportAcceptedRow(i + 1, entityId, rowLine, rowMappingInt, rowMapping ?? category, warning));
        }

        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', accepted.Select(x => x.EntityId))))).ToLowerInvariant();
        return new ImportPreviewResponse(
            accepted.Count,
            rejected.Count,
            duplicateCount,
            checksum,
            category,
            machine,
            lineTarget,
            hasLineWarning,
            accepted.ToArray(),
            rejected.Take(50).ToArray()
        );
    }

    public string NormalizeId(string value) => value.Trim().ToUpperInvariant();
    public string NormalizeConfigKey(string value) => value.Trim().ToUpperInvariant();

    private static string NormalizeClassification(string value, string fallback) =>
        value.Trim().Equals("Leakage", StringComparison.OrdinalIgnoreCase) ? "Leakage" :
        value.Trim().Equals("Overkill", StringComparison.OrdinalIgnoreCase) ? "Overkill" :
        value.Trim().Equals("Monthly", StringComparison.OrdinalIgnoreCase) ? "Monthly" :
        value.Trim().Equals("Weekly", StringComparison.OrdinalIgnoreCase) ? "Weekly" :
        fallback;
}
