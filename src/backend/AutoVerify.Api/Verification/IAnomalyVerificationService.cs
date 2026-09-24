namespace AutoVerify.Api.Verification;

public interface IAnomalyVerificationService
{
    ProductScanItemDto AnalyzeProductAndDefects(ProductRecordDto product, IReadOnlyCollection<DefectRecordDto> defects);
    ProductScanItemDto AnalyzeOverkill(ProductRecordDto product, IReadOnlyCollection<DefectRecordDto> defects);
    ProductScanItemDto AnalyzeLeakage(ProductRecordDto product, IReadOnlyCollection<DefectRecordDto> defects, string lineCode, string? mapping, int? mappingInt);
}
