using AutoVerify.Api.Analytics;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class KMeansEngineTests
{
    [Fact]
    public void Compute_TwoSeparatedClusters_IdentifiesClustersAndHighSilhouette()
    {
        var rows = new List<(int RowIndex, double[] Values, ObservationReference? Observation)>();
        // Cluster 1 around (10, 10)
        for (var i = 0; i < 20; i++)
        {
            var obs = new ObservationReference(Guid.NewGuid(), i, "L1", "M1", null, "RULE1", "v1.0", "Bridge", null);
            rows.Add((i, new[] { 10.0 + (i % 3) * 0.1, 10.0 + (i % 2) * 0.1 }, obs));
        }

        // Cluster 2 around (100, 100)
        for (var i = 20; i < 40; i++)
        {
            var obs = new ObservationReference(Guid.NewGuid(), i, "L1", "M2", null, "RULE2", "v2.0", "Void", null);
            rows.Add((i, new[] { 100.0 + (i % 3) * 0.1, 100.0 + (i % 2) * 0.1 }, obs));
        }

        var result = KMeansEngine.Compute(rows, new[] { "X", "Y" }, requestedK: 2, seed: 123);

        Assert.Equal(2, result.K);
        Assert.Equal(2, result.ClusterSummaries.Count);
        Assert.Equal(40, result.Assignments.Count);
        Assert.True(result.Converged);
        Assert.NotNull(result.SilhouetteScore);
        Assert.True(result.SilhouetteScore.Value > 0.8, $"Silhouette was {result.SilhouetteScore.Value}");

        // Ensure both clusters received 20 points
        Assert.Equal(20, result.ClusterSummaries[0].Size);
        Assert.Equal(20, result.ClusterSummaries[1].Size);
    }

    [Fact]
    public void Compute_SeededExecution_IsDeterministic()
    {
        var rows = new List<(int RowIndex, double[] Values, ObservationReference? Observation)>();
        for (var i = 0; i < 30; i++)
        {
            rows.Add((i, new[] { (double)(i * i % 17), (double)(i * 3 % 13) }, null));
        }

        var res1 = KMeansEngine.Compute(rows, new[] { "A", "B" }, requestedK: 3, seed: 42);
        var res2 = KMeansEngine.Compute(rows, new[] { "A", "B" }, requestedK: 3, seed: 42);

        Assert.Equal(res1.Inertia, res2.Inertia);
        Assert.Equal(res1.Iterations, res2.Iterations);
        for (var i = 0; i < rows.Count; i++)
        {
            Assert.Equal(res1.Assignments[i].ClusterIndex, res2.Assignments[i].ClusterIndex);
        }
    }

    [Fact]
    public void Compute_RequestedKGreaterThanN_ClampsKToN()
    {
        var rows = new List<(int RowIndex, double[] Values, ObservationReference? Observation)>();
        for (var i = 0; i < 5; i++)
        {
            rows.Add((i, new[] { (double)i, (double)i * 2 }, null));
        }

        var res = KMeansEngine.Compute(rows, new[] { "X", "Y" }, requestedK: 10, seed: 42);

        Assert.Equal(5, res.K);
        Assert.Equal(5, res.ClusterSummaries.Count);
        Assert.Equal(5, res.Assignments.Count);
    }

    [Fact]
    public void Compute_ZeroVarianceData_ExecutesWithoutStandardizationDivisionByZero()
    {
        var rows = new List<(int RowIndex, double[] Values, ObservationReference? Observation)>();
        for (var i = 0; i < 10; i++)
        {
            rows.Add((i, new[] { 50.0, 50.0 }, null));
        }

        var res = KMeansEngine.Compute(rows, new[] { "X", "Y" }, requestedK: 2, seed: 42);

        Assert.NotNull(res);
        Assert.False(double.IsNaN(res.Inertia));
    }
}
