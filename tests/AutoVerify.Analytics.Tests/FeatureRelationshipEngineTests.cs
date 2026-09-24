using AutoVerify.Api.Analytics;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class FeatureRelationshipEngineTests
{
    [Fact]
    public void Compute_PerfectPositiveLinearCorrelation_ReturnsPearsonOne()
    {
        var rows = new List<(int RowIndex, double?[] Values, ObservationReference? Observation)>();
        for (var i = 1; i <= 20; i++)
        {
            var obs = new ObservationReference(Guid.NewGuid(), i, "L1", "M1", "S1", "RULE1", "v1.0", "Bridge", "Critical");
            rows.Add((i, new double?[] { i * 2.0, i * 4.0 }, obs));
        }

        var result = FeatureRelationshipEngine.Compute(rows, new[] { "FeatureA", "FeatureB" });

        Assert.Equal(2, result.Features.Count);
        Assert.Single(result.RankedPairs);
        var pair = result.RankedPairs[0];
        Assert.NotNull(pair.Pearson);
        Assert.True(Math.Abs(pair.Pearson.Value - 1.0) < 1e-3);
        Assert.NotNull(pair.Spearman);
        Assert.True(Math.Abs(pair.Spearman.Value - 1.0) < 1e-3);
        Assert.Equal("very_strong", pair.Strength);
        Assert.Equal(20, pair.OverlapCount);
        Assert.NotEmpty(result.ScatterPoints);
        Assert.Equal(20, result.ScatterPoints.Count);
    }

    [Fact]
    public void Compute_MonotonicNonLinearRelationship_SpearmanIsOnePearsonIsLower()
    {
        var rows = new List<(int RowIndex, double?[] Values, ObservationReference? Observation)>();
        for (var i = 1; i <= 15; i++)
        {
            // Exponential relationship: y = exp(x)
            rows.Add((i, new double?[] { (double)i, Math.Exp(i / 3.0) }, null));
        }

        var result = FeatureRelationshipEngine.Compute(rows, new[] { "X", "ExpX" });

        var pair = result.RankedPairs[0];
        Assert.NotNull(pair.Spearman);
        Assert.True(pair.Spearman.Value > 0.99); // monotonic ranks
        Assert.NotNull(pair.Pearson);
        Assert.True(pair.Pearson.Value < pair.Spearman.Value); // linear correlation lower than rank correlation
    }

    [Fact]
    public void Compute_ZeroVarianceFeature_PearsonIsZeroWithoutException()
    {
        var rows = new List<(int RowIndex, double?[] Values, ObservationReference? Observation)>();
        for (var i = 1; i <= 10; i++)
        {
            rows.Add((i, new double?[] { 5.0, (double)i }, null)); // Feature 0 is constant
        }

        var result = FeatureRelationshipEngine.Compute(rows, new[] { "ConstVal", "VarVal" });

        var pair = result.RankedPairs[0];
        Assert.Equal(0.0, pair.Pearson);
        Assert.Equal(10, pair.OverlapCount);
    }

    [Fact]
    public void Compute_MaxScatterPoints_CapsOutputEvenly()
    {
        var rows = new List<(int RowIndex, double?[] Values, ObservationReference? Observation)>();
        for (var i = 1; i <= 200; i++)
        {
            rows.Add((i, new double?[] { (double)i, (double)i }, null));
        }

        var result = FeatureRelationshipEngine.Compute(rows, new[] { "F1", "F2" }, maxScatterPoints: 30);

        Assert.True(result.ScatterPoints.Count <= 30);
    }

    [Fact]
    public void Compute_InsufficientOverlapPoints_ReturnsNullPearsonAndInsufficientData()
    {
        // 5 rows, but only 2 rows have both features present
        var rows = new List<(int RowIndex, double?[] Values, ObservationReference? Observation)>
        {
            (1, new double?[] { 1.0, 2.0 }, null),
            (2, new double?[] { 3.0, 4.0 }, null),
            (3, new double?[] { 5.0, null }, null),
            (4, new double?[] { null, 6.0 }, null),
            (5, new double?[] { null, null }, null)
        };

        var result = FeatureRelationshipEngine.Compute(rows, new[] { "FeatA", "FeatB" });

        Assert.Single(result.RankedPairs);
        var pair = result.RankedPairs[0];
        Assert.Equal(2, pair.OverlapCount);
        Assert.Null(pair.Pearson);
        Assert.Null(pair.Spearman);
        Assert.Equal("insufficient_data", pair.Strength);
    }
}
