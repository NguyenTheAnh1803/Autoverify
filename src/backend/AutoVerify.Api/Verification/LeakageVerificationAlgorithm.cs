namespace AutoVerify.Api.Verification;

public static class LeakageVerificationAlgorithm
{
    public static ProductScanItemDto AnalyzeLeakage(ProductRecordDto product, IReadOnlyCollection<DefectRecordDto> defects, string lineCode, string? mapping, int? mappingInt)
    {
        var judge = product.Judge;
        var isGood = judge.Equals("G", StringComparison.OrdinalIgnoreCase)
            || judge.Equals("OK", StringComparison.OrdinalIgnoreCase)
            || judge.Equals("GOOD", StringComparison.OrdinalIgnoreCase)
            || judge.Equals("PASS", StringComparison.OrdinalIgnoreCase);

        if (!isGood)
        {
            return new ProductScanItemDto(
                product,
                defects,
                defects.Count,
                "Skipped",
                0,
                $"Leakage verify only evaluates items judged G / OK. Current station judge is '{judge}'."
            );
        }

        // Search and correlate defect features according to mapping on this line
        var matchedDefects = defects.Where(d =>
        {
            // If mappingInt is 1, check Feature 1 or severe defects
            if (mappingInt == 1)
            {
                return !string.IsNullOrWhiteSpace(d.FeatureJson) || !string.IsNullOrWhiteSpace(d.Feature1StorageUri)
                    || d.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase)
                    || d.Severity.Equals("Major", StringComparison.OrdinalIgnoreCase);
            }
            // If mappingInt is 2, check Feature 2
            if (mappingInt == 2)
            {
                return !string.IsNullOrWhiteSpace(d.CustomFeatureJson) || !string.IsNullOrWhiteSpace(d.Feature2StorageUri);
            }
            // If custom text mapping provided
            if (!string.IsNullOrWhiteSpace(mapping) && !mapping.Equals("mappingInt", StringComparison.OrdinalIgnoreCase) && !mapping.Equals("entityId", StringComparison.OrdinalIgnoreCase))
            {
                return d.DefectTable.Contains(mapping, StringComparison.OrdinalIgnoreCase)
                    || d.DefectType.Contains(mapping, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(d.ComponentName) && d.ComponentName.Contains(mapping, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(d.FeatureJson) && d.FeatureJson.Contains(mapping, StringComparison.OrdinalIgnoreCase));
            }

            // General feature probe: defect records with features or critical/major severity
            return !string.IsNullOrWhiteSpace(d.FeatureJson)
                || !string.IsNullOrWhiteSpace(d.CustomFeatureJson)
                || d.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase)
                || d.Severity.Equals("Major", StringComparison.OrdinalIgnoreCase)
                || d.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase);
        }).ToList();

        var mappingDesc = mappingInt != null ? $"Mapping #{mappingInt}" : (mapping ?? "Default");

        if (matchedDefects.Count > 0)
        {
            var tables = string.Join(", ", matchedDefects.Select(d => d.DefectTable).Distinct(StringComparer.OrdinalIgnoreCase));
            return new ProductScanItemDto(
                product,
                matchedDefects,
                matchedDefects.Count,
                "Leakage",
                0.95,
                $"Station judged '{judge}' (G/OK) on Line '{lineCode}', but detected {matchedDefects.Count} defect feature(s) matching [{mappingDesc}] in defect table(s): {tables} (Critical Escape Risk / Leakage Detected)."
            );
        }

        return new ProductScanItemDto(
            product,
            [],
            0,
            "ConsistentOK",
            0.01,
            $"Clean product: Station judged '{judge}' (G/OK) and 0 defect features detected for Line '{lineCode}', [{mappingDesc}]."
        );
    }
}
