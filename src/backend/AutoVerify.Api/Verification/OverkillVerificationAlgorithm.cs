namespace AutoVerify.Api.Verification;

public static class OverkillVerificationAlgorithm
{
    public static ProductScanItemDto AnalyzeOverkill(ProductRecordDto product, IReadOnlyCollection<DefectRecordDto> defects)
    {
        // Only evaluate defects judged NG / N (filter out OK, G, FD, etc.)
        var ngDefects = defects.Where(d =>
            d.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase)
            || d.Judge.Equals("N", StringComparison.OrdinalIgnoreCase)
            || d.Judge.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || d.Judge.Equals("NA", StringComparison.OrdinalIgnoreCase)
        ).ToList();

        var totalDefects = ngDefects.Count;
        var judge = product.Judge;
        var isNg = judge.Equals("NG", StringComparison.OrdinalIgnoreCase)
            || judge.Equals("N", StringComparison.OrdinalIgnoreCase)
            || judge.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || judge.Equals("NA", StringComparison.OrdinalIgnoreCase);

        if (!isNg)
        {
            return new ProductScanItemDto(
                product,
                ngDefects,
                totalDefects,
                "Skipped",
                0,
                $"Overkill verify only evaluates items judged N / NG / NA. Current station judge is '{judge}'."
            );
        }

        if (totalDefects == 0)
        {
            return new ProductScanItemDto(
                product,
                ngDefects,
                0,
                "Overkill",
                0.88,
                $"Station judged '{judge}' (NG) but 0 defect records found across all defect tables (False Alarm / Overkill)."
            );
        }

        var tableCount = ngDefects.Select(d => d.DefectTable).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return new ProductScanItemDto(
            product,
            ngDefects,
            totalDefects,
            "ConsistentNG",
            0.12,
            $"Confirmed NG: Station judged '{judge}' and verified {totalDefects} defect(s) across {tableCount} linked defect table(s)."
        );
    }
}
