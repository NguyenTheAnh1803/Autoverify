using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Verification;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class AnomalyClassifierEngineTests
{
    private static ProductRecordEntity CreateProduct(string judge, string line = "LINE-1", string machine = "MACHINE-A") =>
        new()
        {
            Id = Guid.NewGuid(),
            KeyId = "KEY-001",
            Judge = judge,
            LineCode = line,
            MachineCode = machine,
            InspectedAtUtc = DateTimeOffset.UtcNow,
            PanelId = "P001",
            NameDefect = "None"
        };

    private static DefectRecordEntity CreateDefect(
        string name,
        string type = "Optical",
        string severity = "Major",
        string judge = "NG",
        double confidence = 0.95,
        string? featureJson = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            KeyId = "KEY-001",
            DefectTable = "AOI_DEFECTS",
            DefectName = name,
            DefectType = type,
            Severity = severity,
            Judge = judge,
            ConfidenceScore = confidence,
            FeatureJson = featureJson,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

    [Fact]
    public void EvaluateProduct_StationNg_WithoutScript_ClassifiedAsNA()
    {
        var prod = CreateProduct("NG");
        var defect = CreateDefect("UnknownDefect");
        var rules = Array.Empty<MachineClassifierRuleEntity>();

        var result = AnomalyClassifierEngine.EvaluateProduct(prod, [defect], rules);

        // Without script, cannot classify overkill or real NG -> must be NA
        Assert.Equal("NA", result.AnomalyCategory);
        Assert.Equal(0, result.TrueDefectsCount);
        Assert.Contains("NA", result.VerdictReason);
    }

    [Fact]
    public void EvaluateProduct_StationNg_WhenScriptReturnsFalse_ClassifiedAsNA()
    {
        var prod = CreateProduct("NG");
        var featureJson = "{\"G010_VpID\": 2}"; // Fails guard VpID != 1
        var defect = CreateDefect("DIC_Crack", "Optical", "Major", "NG", 0.95, featureJson);

        var script = """
            if( G010_VpID != 1):
            	return False
            Cond1 = ( G020_PxlArea > 90 )
            return Cond1
            """;

        var rule = new MachineClassifierRuleEntity
        {
            Id = Guid.NewGuid(),
            RuleCode = "DIC_Crack",
            RuleName = "DIC_Crack",
            Priority = 1,
            DefectClass = "DIC_Crack",
            ScriptContent = script,
            IsActive = true
        };

        var result = AnomalyClassifierEngine.EvaluateProduct(prod, [defect], [rule]);

        // When script returns false, no overkill algorithm exists yet -> category is NA
        Assert.Equal("NA", result.AnomalyCategory);
        Assert.Equal(0, result.TrueDefectsCount);
        Assert.Contains("NA", result.VerdictReason);
    }

    [Fact]
    public void EvaluateProduct_StationPass_WhenScriptPasses_ClassifiedAsLeakage()
    {
        var prod = CreateProduct("OK"); // Station passed the unit!
        var featureJson = "{\"G010_VpID\": 1, \"G008_LightID\": 1, \"G009_RoiID\": 24, \"G062_OrgGrayMax\": 30, \"G064_OrgGrayStdDev\": 3}";
        var defect = CreateDefect("Light_Fail", "Optical", "Critical", "NG", 0.98, featureJson);

        var lightFailScript = """
            MCL_Position1 = (G010_VpID==1) and (G008_LightID ==1) and (G009_RoiID == 24 or G009_RoiID == 25)
            MCL_Light1 = (G062_OrgGrayMax < 40 and G064_OrgGrayStdDev < 5)
            MCL_Block1 = MCL_Position1 and MCL_Light1
            return MCL_Block1
            """;

        var rule = new MachineClassifierRuleEntity
        {
            Id = Guid.NewGuid(),
            RuleCode = "Light_Fail",
            RuleName = "Light_Fail",
            Priority = 0,
            DefectClass = "Light_Fail",
            ScriptContent = lightFailScript,
            IsActive = true
        };

        var result = AnomalyClassifierEngine.EvaluateProduct(prod, [defect], [rule]);

        Assert.Equal("Leakage", result.AnomalyCategory);
        Assert.Equal(1, result.TrueDefectsCount);
        Assert.Contains("Leakage", result.VerdictReason);
    }

    [Fact]
    public void EvaluateProduct_WithAoiScript_RealNG_WhenScriptPasses()
    {
        var prod = CreateProduct("NG");
        var featureJson = "{\"G020_PxlArea\": 120, \"G017_PxlWidth\": 30, \"G018_PxlHeight\": 30, \"G038_PrcGrayMin\": 30, \"MLClass01\": 95, \"G010_VpID\": 1, \"G008_LightID\": 1, \"G009_RoiID\": 1, \"V003_VP_IP\": 1}";
        var defect = CreateDefect("DIC_Crack", "Optical", "Major", "NG", 0.95, featureJson);

        var script = """
            if( G010_VpID != 1):
            	return False
            Cond1 = ( G020_PxlArea > 90 )
            Cond2 = ( G038_PrcGrayMin < 45 )
            DL_Cond = MLClass01 > 90
            return Cond1 and Cond2 and DL_Cond
            """;

        var rule = new MachineClassifierRuleEntity
        {
            Id = Guid.NewGuid(),
            RuleCode = "DIC_Crack",
            RuleName = "DIC_Crack",
            Priority = 1,
            DefectClass = "DIC_Crack",
            ScriptContent = script,
            IsActive = true
        };

        var result = AnomalyClassifierEngine.EvaluateProduct(prod, [defect], [rule]);

        Assert.Equal("RealNG", result.AnomalyCategory);
        Assert.Equal(1, result.TrueDefectsCount);
        Assert.Contains("Real NG", result.VerdictReason);
    }
}
