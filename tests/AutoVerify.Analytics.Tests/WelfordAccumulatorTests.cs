using AutoVerify.Api.Analytics;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class WelfordAccumulatorTests
{
    [Fact]
    public void Accumulator_ComputesCorrectMeanAndSampleVariance()
    {
        var acc = new WelfordAccumulator();
        double[] values = [2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0];

        foreach (var v in values)
        {
            acc.Add(v);
        }

        Assert.Equal(8, acc.Count);
        Assert.Equal(5.0, acc.Mean, 6);
        Assert.Equal(2.0, acc.Min, 6);
        Assert.Equal(9.0, acc.Max, 6);
        // Sample variance for [2, 4, 4, 4, 5, 5, 7, 9]:
        // mean = 5.0, deviations = [-3, -1, -1, -1, 0, 0, 2, 4]
        // sq deviations = [9, 1, 1, 1, 0, 0, 4, 16] = 32
        // sample variance = 32 / (8 - 1) = 32 / 7 ≈ 4.57142857
        Assert.Equal(32.0 / 7.0, acc.Variance, 6);
        Assert.Equal(Math.Sqrt(32.0 / 7.0), acc.StandardDeviation, 6);
    }

    [Fact]
    public void Accumulator_HandlesEmptyAndSingleItem()
    {
        var acc = new WelfordAccumulator();
        Assert.Equal(0, acc.Count);
        Assert.Equal(0.0, acc.Mean);
        Assert.Equal(0.0, acc.Variance);
        Assert.Equal(0.0, acc.StandardDeviation);

        acc.Add(42.0);
        Assert.Equal(1, acc.Count);
        Assert.Equal(42.0, acc.Mean);
        Assert.Equal(42.0, acc.Min);
        Assert.Equal(42.0, acc.Max);
        Assert.Equal(0.0, acc.Variance);
    }
}
