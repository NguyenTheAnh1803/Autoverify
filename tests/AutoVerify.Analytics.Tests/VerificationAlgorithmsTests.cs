using AutoVerify.Api.Verification;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class VerificationAlgorithmsTests
{
    private static ProductRecordDto CreateProduct(string judge, string nameDefect = "None") =>
        new(
            Id: Guid.NewGuid(),
            KeyId: "PROD-001",
            Judge: judge,
            PanelId: "PANEL-A",
            PanelJudge: judge,
            NameDefect: nameDefect,
            MachineCode: "AOI-01",
            LineCode: "LINE-1",
            InspectedAtUtc: DateTimeOffset.UtcNow,
            MetadataJson: "{}"
        );

    private static DefectRecordDto CreateDefect(string name, string judge = "NG", string severity = "Fatal") =>
        new(
            Id: Guid.NewGuid(),
            KeyId: "PROD-001",
            DefectTable: "tbl_defect",
            DefectName: name,
            DefectType: "SolderBridge",
            ComponentName: "R10",
            CoordinateX: 12.5,
            CoordinateY: 34.2,
            Severity: severity,
            Judge: judge,
            ConfidenceScore: 0.98,
            ImageUri: "seaweedfs://defects/img1.jpg",
            Feature1StorageUri: null,
            Feature2StorageUri: null,
            CreatedAtUtc: DateTimeOffset.UtcNow
        );

    [Fact]
    public void OverkillAlgorithm_WhenProductPasses_ReturnsSkippedStatus()
    {
        var product = CreateProduct("G");
        var defects = Array.Empty<DefectRecordDto>();

        var result = OverkillVerificationAlgorithm.AnalyzeOverkill(product, defects);

        Assert.Equal("Skipped", result.AnomalyStatus);
        Assert.Equal(0.0, result.AnomalyScore);
        Assert.Equal(0, result.TotalDefects);
    }

    [Fact]
    public void OverkillAlgorithm_WhenProductFailsWithoutDefects_FlagsOverkillAnomaly()
    {
        var product = CreateProduct("N", "UnknownNG");
        var defects = Array.Empty<DefectRecordDto>();

        var result = OverkillVerificationAlgorithm.AnalyzeOverkill(product, defects);

        Assert.Equal("Overkill", result.AnomalyStatus);
        Assert.True(result.AnomalyScore >= 0.7);
        Assert.Contains("Overkill", result.AnalysisNotes);
    }

    [Fact]
    public void OverkillAlgorithm_WhenProductFailsWithDefects_ReturnsConsistentNG()
    {
        var product = CreateProduct("N");
        var defects = new[] { CreateDefect("ShortCircuit") };

        var result = OverkillVerificationAlgorithm.AnalyzeOverkill(product, defects);

        Assert.Equal("ConsistentNG", result.AnomalyStatus);
        Assert.Equal(0.12, result.AnomalyScore);
        Assert.Equal(1, result.TotalDefects);
    }

    [Fact]
    public void OverkillAlgorithm_WhenProductFailsWithOnlyOKDefects_FlagsOverkillAnomaly()
    {
        var product = CreateProduct("NG");
        var defects = new[]
        {
            CreateDefect("FalseNoise", judge: "OK"),
            CreateDefect("GoodPass", judge: "G")
        };

        var result = OverkillVerificationAlgorithm.AnalyzeOverkill(product, defects);

        // Even though 2 defect rows exist, both are OK/G, so real NG count is 0 -> Overkill!
        Assert.Equal("Overkill", result.AnomalyStatus);
        Assert.Equal(0, result.TotalDefects);
        Assert.True(result.AnomalyScore >= 0.7);
    }

    [Fact]
    public void OverkillAlgorithm_WhenProductHasMixOfNgAndOkDefects_OnlyCountsNgDefects()
    {
        var product = CreateProduct("N");
        var defects = new[]
        {
            CreateDefect("RealCrack", judge: "NG"),
            CreateDefect("FalseNoise", judge: "OK")
        };

        var result = OverkillVerificationAlgorithm.AnalyzeOverkill(product, defects);

        Assert.Equal("ConsistentNG", result.AnomalyStatus);
        Assert.Equal(1, result.TotalDefects);
        Assert.Single(result.Defects);
        Assert.Equal("RealCrack", result.Defects.First().DefectName);
    }

    [Fact]
    public void LeakageAlgorithm_WhenDefectsFoundOnGoodProduct_FlagsLeakage()
    {
        var product = CreateProduct("G"); // Marked Good by upstream
        var defects = new[] { CreateDefect("Crack", "NG", "Fatal") };

        // mapping: null uses general probe which catches Major/Fatal severity or NG judge
        var result = LeakageVerificationAlgorithm.AnalyzeLeakage(
            product,
            defects,
            lineCode: "LINE-1",
            mapping: null,
            mappingInt: null
        );

        Assert.Equal("Leakage", result.AnomalyStatus);
        Assert.True(result.AnomalyScore >= 0.9);
        Assert.Contains("Critical Escape Risk / Leakage Detected", result.AnalysisNotes);
    }

    [Fact]
    public void LeakageAlgorithm_WhenNoDefectsFoundOnGoodProduct_ReturnsConsistentOK()
    {
        var product = CreateProduct("G");
        var defects = Array.Empty<DefectRecordDto>();

        var result = LeakageVerificationAlgorithm.AnalyzeLeakage(
            product,
            defects,
            lineCode: "LINE-1",
            mapping: null,
            mappingInt: null
        );

        Assert.Equal("ConsistentOK", result.AnomalyStatus);
        Assert.Equal(0.01, result.AnomalyScore);
    }

    [Fact]
    public void AnomalyVerificationService_DelegatesCorrectly()
    {
        IAnomalyVerificationService service = new AnomalyVerificationService();
        var product = CreateProduct("G");
        var defects = new[] { CreateDefect("MissingComponent") };

        var result = service.AnalyzeLeakage(product, defects, "LINE-1", null, null);

        Assert.Equal("Leakage", result.AnomalyStatus);
        Assert.True(result.AnomalyScore > 0.0);
    }
}
