using AutoVerify.Api.Analytics;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class RuleTuningEngineTests
{
    [Fact]
    public void AnalyzeFeatureDistributions_DetectsCompletelySeparatedGoldThreshold()
    {
        var samples = new List<RuleTuningEngine.FeatureSample>
        {
            // Overkill samples (OP judged OK): PxlArea in [20, 35]
            new("K1", "DIC_Crack", false, new Dictionary<string, double> { ["G020_PxlArea"] = 22.0, ["G038_PrcGrayMin"] = 40.0 }),
            new("K2", "DIC_Crack", false, new Dictionary<string, double> { ["G020_PxlArea"] = 30.0, ["G038_PrcGrayMin"] = 42.0 }),
            new("K3", "DIC_Crack", false, new Dictionary<string, double> { ["G020_PxlArea"] = 35.0, ["G038_PrcGrayMin"] = 38.0 }),

            // Real NG samples (OP confirmed NG): PxlArea in [70, 150]
            new("K4", "DIC_Crack", true, new Dictionary<string, double> { ["G020_PxlArea"] = 75.0, ["G038_PrcGrayMin"] = 25.0 }),
            new("K5", "DIC_Crack", true, new Dictionary<string, double> { ["G020_PxlArea"] = 90.0, ["G038_PrcGrayMin"] = 20.0 }),
            new("K6", "DIC_Crack", true, new Dictionary<string, double> { ["G020_PxlArea"] = 120.0, ["G038_PrcGrayMin"] = 18.0 }),
        };

        var script = "Cond1 = ( G020_PxlArea > 20 )\nreturn Cond1";
        var results = RuleTuningEngine.AnalyzeFeatureDistributions(samples, script);

        var areaResult = results.FirstOrDefault(r => r.FeatureName == "G020_PxlArea");
        Assert.NotNull(areaResult);
        Assert.Equal("CompletelySeparated", areaResult.SeparationKind);
        Assert.Equal(35.0, areaResult.OverkillMax);
        Assert.Equal(75.0, areaResult.RealMin);

        // Gold threshold should be midpoint: (35 + 75) / 2 = 55.0
        Assert.Equal(55.0, areaResult.ProposedThreshold);
        Assert.Equal(100.0, areaResult.ProjectedOverkillCutPercent);
        Assert.Equal(20.0, areaResult.CurrentThresholdInScript);
    }

    [Fact]
    public void GenerateTunedScript_UpdatesSensitiveThresholdsInScript()
    {
        var separations = new List<RuleTuningEngine.FeatureSeparationResult>
        {
            new(
                "G020_PxlArea",
                20.0,
                55.0,
                ">",
                20.0, 35.0, 28.0,
                75.0, 120.0, 95.0,
                "CompletelySeparated",
                100.0,
                "G020_PxlArea > 55.0",
                "Cut 100% overkill"
            )
        };

        var script = "Cond1 = ( G020_PxlArea > 20 )\nreturn Cond1";
        var tuned = RuleTuningEngine.GenerateTunedScript(script, separations);

        Assert.Contains("G020_PxlArea > 55", tuned);
        Assert.Contains("# Optimized [G020_PxlArea]", tuned);
    }

    [Fact]
    public void SimulateWhatIf_ReportsSafeOverkillReduction_WithZeroLeakage()
    {
        var samples = new List<RuleTuningEngine.FeatureSample>
        {
            // Overkill 1: PxlArea = 25 (Baseline catches it, Candidate filters it out)
            new("K1", "DIC_Crack", false, new Dictionary<string, double> { ["G020_PxlArea"] = 25.0 }),
            // Overkill 2: PxlArea = 30 (Baseline catches it, Candidate filters it out)
            new("K2", "DIC_Crack", false, new Dictionary<string, double> { ["G020_PxlArea"] = 30.0 }),
            // Real NG 1: PxlArea = 80 (Both catch it)
            new("K3", "DIC_Crack", true, new Dictionary<string, double> { ["G020_PxlArea"] = 80.0 }),
            // Real NG 2: PxlArea = 100 (Both catch it)
            new("K4", "DIC_Crack", true, new Dictionary<string, double> { ["G020_PxlArea"] = 100.0 }),
        };

        var baselineScript = "Cond1 = ( G020_PxlArea > 20 )\nreturn Cond1";
        var candidateScript = "Cond1 = ( G020_PxlArea > 50 )\nreturn Cond1";

        var sim = RuleTuningEngine.SimulateWhatIf(baselineScript, candidateScript, samples);

        Assert.Equal(4, sim.TotalEvaluated);
        Assert.Equal(2, sim.BaselineOverkillCount);
        Assert.Equal(0, sim.CandidateOverkillCount);
        Assert.Equal(2, sim.OverkillReducedCount);
        Assert.Equal(100.0, sim.OverkillReductionPercent);

        Assert.Equal(0, sim.CandidateLeakageCount);
        Assert.True(sim.IsSafeToDeploy);
        Assert.Equal(2, sim.Divergences.Count);
        Assert.All(sim.Divergences, d => Assert.Equal("OverkillEliminated", d.DivergenceKind));
    }

    [Fact]
    public void SimulateWhatIf_FlagsDangerousLeakage_WhenThresholdTooStrict()
    {
        var samples = new List<RuleTuningEngine.FeatureSample>
        {
            // Real NG with PxlArea = 80
            new("K1", "DIC_Crack", true, new Dictionary<string, double> { ["G020_PxlArea"] = 80.0 }),
        };

        var baselineScript = "Cond1 = ( G020_PxlArea > 50 )\nreturn Cond1";
        // Candidate threshold = 90 -> misses the real defect with Area 80!
        var candidateScript = "Cond1 = ( G020_PxlArea > 90 )\nreturn Cond1";

        var sim = RuleTuningEngine.SimulateWhatIf(baselineScript, candidateScript, samples);

        Assert.Equal(1, sim.CandidateLeakageCount);
        Assert.False(sim.IsSafeToDeploy);
        Assert.Contains("DANGER", sim.Divergences[0].Explanation);
    }
}
