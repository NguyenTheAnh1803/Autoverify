using AutoVerify.Api.Analytics;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class LttbDownsamplerTests
{
    [Fact]
    public void Downsample_PreservesFirstAndLastPoints_AndMinMax()
    {
        var original = new List<DataPoint>();
        for (int i = 0; i < 1000; i++)
        {
            double y = Math.Sin(i * 0.1);
            if (i == 250) y = 99.0; // Extreme peak
            if (i == 750) y = -99.0; // Extreme trough
            original.Add(new DataPoint(i, y, i));
        }

        int targetThreshold = 50;
        var sampled = LttbDownsampler.Downsample(original, targetThreshold);

        Assert.Equal(targetThreshold, sampled.Count);
        // First and last point preserved
        Assert.Equal(original[0].X, sampled[0].X);
        Assert.Equal(original[0].Y, sampled[0].Y);
        Assert.Equal(original[^1].X, sampled[^1].X);
        Assert.Equal(original[^1].Y, sampled[^1].Y);

        // Max and min preserved or closely captured
        Assert.Contains(sampled, p => p.Y >= 99.0);
        Assert.Contains(sampled, p => p.Y <= -99.0);
    }

    [Fact]
    public void Downsample_ReturnsOriginalIfSmallerThanThreshold()
    {
        var small = new List<DataPoint>
        {
            new(1, 10, 0), new(2, 20, 1), new(3, 15, 2)
        };

        var sampled = LttbDownsampler.Downsample(small, 10);
        Assert.Equal(3, sampled.Count);
    }
}
