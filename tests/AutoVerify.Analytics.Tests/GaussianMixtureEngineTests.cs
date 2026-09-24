using AutoVerify.Api.Analytics;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class GaussianMixtureEngineTests
{
    [Fact]
    public void Compute_TwoComponentMixture_ConvergesAndCalculatesBicAic()
    {
        var rows = new List<(int RowIndex, double[] Values, ObservationReference? Observation)>();
        // Subpopulation 1: mean 5.0
        for (var i = 0; i < 25; i++)
        {
            var obs = new ObservationReference(Guid.NewGuid(), i, "L1", "M1", "S1", "RULE1", "v1.0", "Bridge", null);
            rows.Add((i, new[] { 5.0 + (i % 5) * 0.1 }, obs));
        }

        // Subpopulation 2: mean 50.0
        for (var i = 25; i < 50; i++)
        {
            var obs = new ObservationReference(Guid.NewGuid(), i, "L2", "M3", "S2", "RULE2", "v1.0", "Void", null);
            rows.Add((i, new[] { 50.0 + (i % 5) * 0.1 }, obs));
        }

        var result = GaussianMixtureEngine.Compute(rows, new[] { "Signal" }, requestedComponents: 2, seed: 99);

        Assert.Equal(2, result.ComponentsCount);
        Assert.Equal(2, result.ComponentSummaries.Count);
        Assert.Equal(50, result.Assignments.Count);
        Assert.True(result.Converged);
        Assert.True(result.Bic != 0.0);
        Assert.True(result.Aic != 0.0);

        // Verify responsibilities sum to 1.0 for each observation
        foreach (var assignment in result.Assignments)
        {
            var sum = assignment.Probabilities.Sum();
            Assert.True(Math.Abs(sum - 1.0) < 1e-3, $"Responsibilities sum was {sum}");
        }
    }

    [Fact]
    public void Compute_ZeroSpreadData_RegularizesWithoutSingularity()
    {
        var rows = new List<(int RowIndex, double[] Values, ObservationReference? Observation)>();
        for (var i = 0; i < 15; i++)
        {
            rows.Add((i, new[] { 10.0, 20.0 }, null));
        }

        var result = GaussianMixtureEngine.Compute(rows, new[] { "F1", "F2" }, requestedComponents: 2, seed: 42);

        Assert.NotNull(result);
        Assert.False(double.IsNaN(result.LogLikelihood));
        Assert.False(double.IsInfinity(result.LogLikelihood));
    }

    [Fact]
    public void Compute_ComponentsGreaterThanObservations_ClampsComponentsToN()
    {
        var rows = new List<(int RowIndex, double[] Values, ObservationReference? Observation)>();
        for (var i = 0; i < 4; i++)
        {
            rows.Add((i, new[] { (double)i * 2.0 }, null));
        }

        var result = GaussianMixtureEngine.Compute(rows, new[] { "Signal" }, requestedComponents: 8, seed: 42);

        Assert.Equal(4, result.ComponentsCount);
        Assert.Equal(4, result.ComponentSummaries.Count);
        Assert.Equal(4, result.Assignments.Count);
    }
}
