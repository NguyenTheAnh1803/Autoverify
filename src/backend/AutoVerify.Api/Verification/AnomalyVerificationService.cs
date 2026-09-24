namespace AutoVerify.Api.Verification;

public sealed class AnomalyVerificationService : IAnomalyVerificationService
{
    public ProductScanItemDto AnalyzeProductAndDefects(ProductRecordDto product, IReadOnlyCollection<DefectRecordDto> defects)
    {
        var totalDefects = defects.Count;
        var isProductNg = product.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("N", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("NA", StringComparison.OrdinalIgnoreCase);
        var hasCriticalDefect = defects.Any(d =>
            d.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase) ||
            d.Severity.Equals("Major", StringComparison.OrdinalIgnoreCase) ||
            d.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase)
        );

        string anomalyStatus;
        double anomalyScore;
        string notes;

        if (isProductNg && totalDefects == 0)
        {
            anomalyStatus = "Overkill";
            anomalyScore = 0.88;
            notes = "Station judged NG but 0 defect records found across all defect tables (Suspected False Alarm / Overkill).";
        }
        else if (!isProductNg && hasCriticalDefect)
        {
            anomalyStatus = "Leakage";
            anomalyScore = 0.95;
            notes = "Station judged OK/Pass but Critical/Major defects detected in linked defect tables (Escape risk / Leakage).";
        }
        else if (isProductNg && totalDefects > 0)
        {
            anomalyStatus = "ConsistentNG";
            anomalyScore = 0.12;
            var tableCount = defects.Select(d => d.DefectTable).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            notes = $"Confirmed NG: Verified {totalDefects} defect(s) across {tableCount} linked defect table(s).";
        }
        else
        {
            anomalyStatus = "ConsistentOK";
            anomalyScore = 0.01;
            notes = "Clean product: No defect records detected.";
        }

        return new ProductScanItemDto(product, defects, totalDefects, anomalyStatus, anomalyScore, notes);
    }

    public ProductScanItemDto AnalyzeOverkill(ProductRecordDto product, IReadOnlyCollection<DefectRecordDto> defects) =>
        OverkillVerificationAlgorithm.AnalyzeOverkill(product, defects);

    public ProductScanItemDto AnalyzeLeakage(ProductRecordDto product, IReadOnlyCollection<DefectRecordDto> defects, string lineCode, string? mapping, int? mappingInt) =>
        LeakageVerificationAlgorithm.AnalyzeLeakage(product, defects, lineCode, mapping, mappingInt);
}
