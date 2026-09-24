using AutoVerify.Api.Analytics;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class SpcEngineTests
{
    [Fact]
    public void ComputeImr_DetectsShiftAndNelsonRuleViolations_OnBenchmarkDataset()
    {
        // Benchmark Dataset SPC_IMR_01:
        // 30 points: first 20 points centered at 10.0 with small variance, next 10 points shifted by +2.5
        var raw = new List<double>
        {
            9.9, 10.1, 10.0, 9.8, 10.2, 10.1, 9.9, 10.0, 10.2, 9.8,
            10.0, 10.1, 9.9, 10.2, 9.8, 10.0, 10.1, 9.9, 10.0, 10.1,
            // Shift to ~12.5 starting at index 20
            12.4, 12.6, 12.5, 12.7, 12.3, 12.5, 12.6, 12.4, 12.5, 12.7
        };

        var inputData = raw.Select((val, idx) => (RowIndex: idx, Value: val, SubgroupId: (string?)null)).ToList();

        var result = SpcEngine.Compute(
            inputData,
            featureName: "defect_area",
            chartType: "IMR",
            lsl: 6.0,
            usl: 14.0,
            target: 10.0,
            isNgOnly: false);

        Assert.NotNull(result);
        Assert.Equal(30, result.PrimaryChart.Points.Count);
        Assert.NotNull(result.SecondaryChart);
        Assert.Equal(29, result.SecondaryChart.Points.Count);

        // Grand average Xbar and Rbar should be positive
        Assert.True(result.PrimaryChart.CenterLine > 10.0);
        Assert.True(result.SecondaryChart.CenterLine > 0.0);

        // Verification of Nelson Rule violations:
        // Because of the massive shift from index 20 onwards, we expect violations (Rule 1, 2, or 3)
        var pointsWithViolations = result.PrimaryChart.Points.Where(p => p.ViolatingRuleIds != null && p.ViolatingRuleIds.Count > 0).ToList();
        Assert.NotEmpty(pointsWithViolations);
        // Violations are detected both before and after the mean shift (points outside 3-sigma limits)
        Assert.Contains(pointsWithViolations, p => p.RowIndex >= 20);
        Assert.Contains(pointsWithViolations, p => p.RowIndex < 20);

        // Process capability metrics
        Assert.NotNull(result.Capability);
        Assert.True(result.Capability.Cp > 0);
        Assert.True(result.Capability.Cpk > 0);
        Assert.True(result.Capability.IsValid);
    }

    [Fact]
    public void ComputeImr_TriggersNgOnlyWarning_WhenNgOnlyIsTrue()
    {
        var raw = new List<double> { 10.0, 10.1, 9.9, 10.2, 9.8, 10.0, 10.1, 9.9, 10.0, 10.1 };
        var inputData = raw.Select((val, idx) => (RowIndex: idx, Value: val, SubgroupId: (string?)null)).ToList();

        var result = SpcEngine.Compute(
            inputData,
            featureName: "score",
            chartType: "IMR",
            lsl: 5.0,
            usl: 15.0,
            target: null,
            isNgOnly: true);

        Assert.NotNull(result.Capability);
        Assert.False(result.Capability.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("NG-Only Distribution Guard"));
    }

    [Fact]
    public void ComputeXbar_EvaluatesSubgroupsCorrectly()
    {
        // 5 subgroups of size 3 = 15 observations
        var inputData = new List<(int RowIndex, double Value, string? SubgroupId)>
        {
            (0, 10.0, "SG1"), (1, 10.2, "SG1"), (2, 9.8, "SG1"),
            (3, 10.1, "SG2"), (4, 9.9, "SG2"), (5, 10.0, "SG2"),
            (6, 10.3, "SG3"), (7, 10.1, "SG3"), (8, 10.2, "SG3"),
            (9, 9.7, "SG4"), (10, 9.9, "SG4"), (11, 9.8, "SG4"),
            (12, 10.0, "SG5"), (13, 10.0, "SG5"), (14, 10.0, "SG5")
        };

        var result = SpcEngine.Compute(
            inputData,
            featureName: "thickness",
            chartType: "XbarR",
            lsl: 8.0,
            usl: 12.0,
            target: 10.0);

        Assert.NotNull(result);
        Assert.Equal(5, result.PrimaryChart.Points.Count);
        Assert.Equal(10.0, result.PrimaryChart.CenterLine, 1);
        Assert.True(result.PrimaryChart.UpperControlLimit > result.PrimaryChart.LowerControlLimit);
    }

    [Fact]
    public void ComputeImr_WithConstantValues_DoesNotProduceNaN()
    {
        var raw = new List<double> { 5.0, 5.0, 5.0, 5.0, 5.0 };
        var inputData = raw.Select((val, idx) => (RowIndex: idx, Value: val, SubgroupId: (string?)null)).ToList();

        var result = SpcEngine.Compute(
            inputData,
            featureName: "constant_feature",
            chartType: "IMR",
            lsl: 4.0,
            usl: 6.0,
            target: 5.0);

        Assert.NotNull(result);
        Assert.Equal(5, result.PrimaryChart.Points.Count);
        Assert.Equal(5.0, result.PrimaryChart.CenterLine);
        Assert.False(double.IsNaN(result.PrimaryChart.UpperControlLimit));
        Assert.False(double.IsNaN(result.PrimaryChart.LowerControlLimit));
    }
}
