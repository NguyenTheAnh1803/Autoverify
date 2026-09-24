using AutoVerify.Api.Analytics;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class DoeEngineTests
{
    [Fact]
    public void ComputeFactorialScreening_RanksParetoEffectsCorrectly_OnBenchmarkDataset()
    {
        // Dataset DOE_SCREEN_01:
        // 2^3 full factorial design (8 runs)
        // A, B, C in {-1, +1}
        // Y = 50 + 10*A - 6*B + 1.5*C + 4*A*B
        var factorNames = new List<string> { "A", "B", "C" };
        var runs = new List<(int RowIndex, double Response, Dictionary<string, double> Factors)>();

        int[][] designMatrix = [
            [-1, -1, -1],
            [ 1, -1, -1],
            [-1,  1, -1],
            [ 1,  1, -1],
            [-1, -1,  1],
            [ 1, -1,  1],
            [-1,  1,  1],
            [ 1,  1,  1]
        ];

        for (int i = 0; i < 8; i++)
        {
            double a = designMatrix[i][0];
            double b = designMatrix[i][1];
            double c = designMatrix[i][2];
            // Deterministic response
            double y = 50.0 + 10.0 * a - 6.0 * b + 1.5 * c + 4.0 * a * b;
            var dict = new Dictionary<string, double>
            {
                ["A"] = a,
                ["B"] = b,
                ["C"] = c
            };
            runs.Add((i, y, dict));
        }

        var result = DoeEngine.Compute(
            runs,
            responseName: "DefectRate",
            factorNames: factorNames,
            include2FactorInteractions: true,
            alpha: 0.05);

        Assert.NotNull(result);
        Assert.Equal(8, result.TotalRuns);
        Assert.True(result.Summary.R2 > 0.95);

        // Verification of Pareto Ranking:
        // Coefficient for A = 10 (effect magnitude = 10)
        // Coefficient for B = -6 (effect magnitude = 6)
        // Two-factor A*B = 4 (effect magnitude = 4)
        // Coefficient for C = 1.5 (effect magnitude = 1.5)
        // Expected Pareto order of absolute effect: A > B > A:B > C
        Assert.NotEmpty(result.EffectsPareto);
        Assert.Equal("A", result.EffectsPareto[0].Term);
        Assert.Equal("B", result.EffectsPareto[1].Term);
        Assert.Equal("A:B", result.EffectsPareto[2].Term);
        Assert.Equal("C", result.EffectsPareto[3].Term);

        // Verification of effect estimates
        var effectA = result.EffectsPareto.First(p => p.Term == "A");
        var effectB = result.EffectsPareto.First(p => p.Term == "B");
        var effectAB = result.EffectsPareto.First(p => p.Term == "A:B");
        var effectC = result.EffectsPareto.First(p => p.Term == "C");

        Assert.Equal(10.0, effectA.Coefficient, 2);
        Assert.Equal(-6.0, effectB.Coefficient, 2);
        Assert.Equal(4.0, effectAB.Coefficient, 2);
        Assert.Equal(1.5, effectC.Coefficient, 2);
    }
}
