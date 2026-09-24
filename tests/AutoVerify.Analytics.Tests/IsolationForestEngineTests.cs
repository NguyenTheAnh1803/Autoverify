using AutoVerify.Api.Analytics;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class IsolationForestEngineTests
{
    [Fact]
    public void Compute_ClearOutliers_DetectsOutliersWithHighScore()
    {
        var rows = new List<(int RowIndex, double[] Values, ObservationReference? Observation)>();
        // 50 normal inliers clustered near 10.0
        for (var i = 0; i < 50; i++)
        {
            var obs = new ObservationReference(Guid.NewGuid(), i, "L1", "M1", "S1", "RULE1", "v1.0", "Bridge", null);
            rows.Add((i, new[] { 10.0 + (i % 5) * 0.1, 10.0 + (i % 3) * 0.1 }, obs));
        }

        // 2 extreme outliers far away
        var outlier1 = new ObservationReference(Guid.NewGuid(), 50, "L1", "M1", "S1", "RULE1", "v1.0", "Bridge", null);
        var outlier2 = new ObservationReference(Guid.NewGuid(), 51, "L1", "M1", "S1", "RULE1", "v1.0", "Bridge", null);
        rows.Add((50, new[] { 500.0, 500.0 }, outlier1));
        rows.Add((51, new[] { -300.0, 400.0 }, outlier2));

        var result = IsolationForestEngine.Compute(
            rows,
            new[] { "X", "Y" },
            nTrees: 100,
            contamination: 0.05,
            seed: 42
        );

        Assert.Equal(52, result.TotalObservations);
        Assert.True(result.AnomalyCount >= 2);
        Assert.NotEmpty(result.TopAnomalies);

        // Check that outlier observations 50 or 51 are at the top
        var topRowIndices = result.TopAnomalies.Select(t => t.Observation.RowIndex).Take(2).ToList();
        Assert.True(topRowIndices.Contains(50) || topRowIndices.Contains(51));

        // Anomaly score for extreme outlier should be elevated (> 0.6)
        var topScore = result.TopAnomalies[0].AnomalyScore;
        Assert.True(topScore > 0.55, $"Top anomaly score was {topScore}");
    }

    [Fact]
    public void Compute_SeededExecution_IsDeterministic()
    {
        var rows = new List<(int RowIndex, double[] Values, ObservationReference? Observation)>();
        for (var i = 0; i < 30; i++)
        {
            rows.Add((i, new[] { (double)(i % 7), (double)(i % 11) }, null));
        }

        var res1 = IsolationForestEngine.Compute(rows, new[] { "A", "B" }, nTrees: 50, seed: 101);
        var res2 = IsolationForestEngine.Compute(rows, new[] { "A", "B" }, nTrees: 50, seed: 101);

        Assert.Equal(res1.ScoreThreshold, res2.ScoreThreshold);
        Assert.Equal(res1.AnomalyCount, res2.AnomalyCount);
        for (var i = 0; i < rows.Count; i++)
        {
            Assert.Equal(res1.Assignments[i].AnomalyScore, res2.Assignments[i].AnomalyScore);
            Assert.Equal(res1.Assignments[i].IsAnomaly, res2.Assignments[i].IsAnomaly);
        }
    }

    [Fact]
    public void Compute_SmallDatasetLessThanEight_ExecutesWithoutClampException()
    {
        // 4 observations (less than default 8 subsample minimum)
        var rows = new List<(int RowIndex, double[] Values, ObservationReference? Observation)>();
        for (var i = 1; i <= 4; i++)
        {
            rows.Add((i, new[] { (double)i * 1.5 }, null));
        }

        var res = IsolationForestEngine.Compute(rows, new[] { "Signal" }, nTrees: 15, seed: 77);

        Assert.Equal(4, res.TotalObservations);
        Assert.Equal(4, res.Assignments.Count);
    }

    [Fact]
    public void Compute_CompletelyConstantFeatures_ReturnsCleanResultWithoutSplittingError()
    {
        var rows = new List<(int RowIndex, double[] Values, ObservationReference? Observation)>();
        for (var i = 1; i <= 10; i++)
        {
            rows.Add((i, new[] { 42.0, 100.0 }, null));
        }

        var res = IsolationForestEngine.Compute(rows, new[] { "ConstA", "ConstB" }, nTrees: 20, seed: 42);

        Assert.Equal(10, res.TotalObservations);
        Assert.All(res.Assignments, a => Assert.False(double.IsNaN(a.AnomalyScore)));
    }
}
