using AutoVerify.Api.Analytics;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class TimeSeriesEngineTests
{
    [Fact]
    public void Ewma_DetectsThreeSigmaShift_OnBenchmarkDataset()
    {
        // Dataset EWMA_CUSUM_01:
        // N = 50, points 0..34 ~ 5.0, points 35..49 ~ 5.6 (a +0.6 shift = 3-sigma where sigma = 0.2)
        var inputData = new List<(int RowIndex, double Value)>();
        for (int i = 0; i < 35; i++)
        {
            inputData.Add((i, 5.0 + (i % 3 == 0 ? 0.05 : (i % 3 == 1 ? -0.05 : 0.0))));
        }
        for (int i = 35; i < 50; i++)
        {
            inputData.Add((i, 5.6 + (i % 3 == 0 ? 0.05 : (i % 3 == 1 ? -0.05 : 0.0))));
        }

        var result = TimeSeriesEngine.ComputeEwma(
            inputData,
            featureName: "dimension_x",
            lambda: 0.2,
            lFactor: 3.0,
            target: 5.0,
            sigma: 0.2);

        Assert.NotNull(result);
        Assert.Equal(50, result.Points.Count);

        // Verification: EWMA detects out-of-control points after the shift (t >= 35)
        var outOfControlPoints = result.Points.Where(p => p.IsViolation).ToList();
        Assert.NotEmpty(outOfControlPoints);
        Assert.All(outOfControlPoints, p => Assert.True(p.RowIndex >= 35));
        // First out of control point should occur within [36, 40]
        var firstOoc = outOfControlPoints.First();
        Assert.InRange(firstOoc.RowIndex, 36, 40);
    }

    [Fact]
    public void Cusum_DetectsThreeSigmaShift_OnBenchmarkDataset()
    {
        var inputData = new List<(int RowIndex, double Value)>();
        for (int i = 0; i < 35; i++)
        {
            inputData.Add((i, 5.0 + (i % 3 == 0 ? 0.05 : (i % 3 == 1 ? -0.05 : 0.0))));
        }
        for (int i = 35; i < 50; i++)
        {
            inputData.Add((i, 5.6 + (i % 3 == 0 ? 0.05 : (i % 3 == 1 ? -0.05 : 0.0))));
        }

        var result = TimeSeriesEngine.ComputeCusum(
            inputData,
            featureName: "dimension_x",
            target: 5.0,
            sigma: 0.2,
            kParam: 0.5,
            hParam: 4.0);

        Assert.NotNull(result);
        Assert.Equal(50, result.Points.Count);

        var outOfControlPoints = result.Points.Where(p => p.IsViolationHigh || p.IsViolationLow).ToList();
        Assert.NotEmpty(outOfControlPoints);
        Assert.All(outOfControlPoints, p => Assert.True(p.RowIndex >= 35));
        // First out of control should occur shortly after t = 35
        var firstOoc = outOfControlPoints.First();
        Assert.InRange(firstOoc.RowIndex, 36, 40);
    }

    [Fact]
    public void Ewma_WithConstantValues_DoesNotProduceViolations()
    {
        var inputData = Enumerable.Range(0, 20).Select(i => (RowIndex: i, Value: 10.0)).ToList();
        var result = TimeSeriesEngine.ComputeEwma(inputData, "constant", lambda: 0.2, lFactor: 3.0, target: 10.0, sigma: 0.1);

        Assert.NotNull(result);
        Assert.Equal(20, result.Points.Count);
        Assert.All(result.Points, p => Assert.False(p.IsViolation));
        Assert.All(result.Points, p => Assert.Equal(10.0, p.EwmaValue, 5));
    }
}
