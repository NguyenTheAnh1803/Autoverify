using AutoVerify.Api.Analytics;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class PcaEngineTests
{
    [Fact]
    public void ComputePca_ExplainsDominantVariance_AndDetectsHotellingAndSpeOutliers()
    {
        // Dataset PCA_SVD_01:
        // 100 samples, 5 features (X1, X2, X3, X4, X5)
        // X1, X2, X3 are highly correlated: X1 ~ t, X2 ~ 2*t, X3 ~ -1.5*t
        // X4 is noise
        // X5 has 3 extreme outliers at index 97, 98, 99
        var featureNames = new List<string> { "X1", "X2", "X3", "X4", "X5" };
        var rawData = new List<(int RowIndex, double[] Values)>();

        for (int i = 0; i < 97; i++)
        {
            double t = (i - 50) * 0.1;
            double x1 = t + (i % 2 == 0 ? 0.05 : -0.05);
            double x2 = 2.0 * t + (i % 3 == 0 ? 0.02 : -0.02);
            double x3 = -1.5 * t + (i % 5 == 0 ? 0.03 : -0.03);
            double x4 = ((i % 7) - 3) * 0.1;
            double x5 = ((i % 4) - 2) * 0.1;
            rawData.Add((i, [x1, x2, x3, x4, x5]));
        }

        // Add 3 extreme outliers
        rawData.Add((97, [0.0, 0.0, 0.0, 0.0, 25.0]));
        rawData.Add((98, [0.0, 0.0, 0.0, 0.0, -30.0]));
        rawData.Add((99, [15.0, -15.0, 20.0, 0.0, 35.0]));

        var result = PcaEngine.Compute(
            rawData,
            featureNames,
            requestedComponents: 2,
            confidenceLevel: 0.95,
            maxSampledPoints: 1000);

        Assert.NotNull(result);
        Assert.Equal(2, result.Components.Count);
        Assert.Equal(100, result.SampledPoints.Count);

        // Verification: PC1 explains significant portion of variance (> 40%)
        Assert.True(result.Components[0].ExplainedVarianceRatio > 0.40);

        // Verification: Check that the 3 extreme points (97, 98, 99) are flagged as outliers
        var obs97 = result.SampledPoints.First(o => o.RowIndex == 97);
        var obs98 = result.SampledPoints.First(o => o.RowIndex == 98);
        var obs99 = result.SampledPoints.First(o => o.RowIndex == 99);

        Assert.True(obs97.IsHotellingOutlier || obs97.IsSpeOutlier);
        Assert.True(obs98.IsHotellingOutlier || obs98.IsSpeOutlier);
        Assert.True(obs99.IsHotellingOutlier || obs99.IsSpeOutlier);
    }

    [Fact]
    public void ComputePca_WithCollinearFeatures_ExplainsNearlyAllVarianceInFirstComponent()
    {
        var featureNames = new List<string> { "F1", "F2", "F3" };
        var rawData = new List<(int RowIndex, double[] Values)>
        {
            (1, [1.0, 2.0, 3.0]),
            (2, [2.0, 4.0, 6.0]),
            (3, [3.0, 6.0, 9.0]),
            (4, [4.0, 8.0, 12.0]),
            (5, [5.0, 10.0, 15.0])
        };

        var result = PcaEngine.Compute(
            rawData,
            featureNames,
            requestedComponents: 2,
            confidenceLevel: 0.95);

        Assert.NotNull(result);
        Assert.Equal(2, result.Components.Count);
        Assert.Equal(5, result.SampledPoints.Count);
        Assert.True(result.Components[0].ExplainedVarianceRatio > 0.95);
    }
}
