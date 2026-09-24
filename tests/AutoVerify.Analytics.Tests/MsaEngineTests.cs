using AutoVerify.Api.Analytics;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class MsaEngineTests
{
    [Fact]
    public void ComputeGageAnova_ComputesVarianceComponentsAndNdc_OnBenchmarkDataset()
    {
        // Dataset MSA_GAGE_01:
        // 10 parts, 3 operators, 3 replicates = 90 rows
        var data = new List<(string PartId, string OperatorId, double Value)>();

        double[] partEffects = [10.0, 15.0, 20.0, 25.0, 30.0, 35.0, 40.0, 45.0, 50.0, 55.0];
        double[] opEffects = [-0.5, 0.0, 0.5];

        for (int p = 0; p < 10; p++)
        {
            for (int o = 0; o < 3; o++)
            {
                for (int r = 0; r < 3; r++)
                {
                    double noise = (r == 0 ? -0.1 : (r == 1 ? 0.0 : 0.1));
                    double measurement = partEffects[p] + opEffects[o] + noise;
                    data.Add(($"Part_{p + 1}", $"Op_{o + 1}", measurement));
                }
            }
        }

        var result = MsaEngine.Compute(
            data,
            featureName: "width",
            tolerance: 60.0,
            alphaInteraction: 0.05);

        Assert.NotNull(result);
        Assert.Equal(10, result.PartCount);
        Assert.Equal(3, result.OperatorCount);
        Assert.Equal(3, result.RepeatCount);
        Assert.Equal(90, result.TotalMeasurements);

        // Verification of ANOVA table
        Assert.NotEmpty(result.AnovaTable);
        var partRow = result.AnovaTable.First(r => r.Source == "Part");
        Assert.Equal(9, partRow.DegreesOfFreedom); // 10 - 1 = 9
        Assert.True(partRow.FStatistic > 10.0); // Strong part discrimination
        Assert.True(partRow.PValue < 0.001);

        // Verification of Gage R&R variance components
        Assert.NotEmpty(result.VarianceComponents);
        var partToPart = result.VarianceComponents.First(vc => vc.Source.Contains("Part-To-Part", StringComparison.OrdinalIgnoreCase));
        var totalGage = result.VarianceComponents.First(vc => vc.Source.Contains("Total Gage", StringComparison.OrdinalIgnoreCase));

        // Part-to-Part variance should dominate given the high spread between parts
        Assert.True(partToPart.Variance > totalGage.Variance);
        Assert.True(partToPart.PercentStudyVar > 80.0);

        // ndc (Number of Distinct Categories) should be >= 5 for acceptable measurement system
        Assert.True(result.NumberOfDistinctCategories >= 5);
        Assert.StartsWith("Acceptable", result.StatusAssessment);
    }
}
