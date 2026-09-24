namespace AutoVerify.Api.Analytics;

/// <summary>
/// Incremental online single-pass computation of count, min, max, mean, and variance
/// using B. P. Welford's algorithm. O(1) memory, numerically stable.
/// </summary>
public sealed class WelfordAccumulator
{
    public long Count { get; private set; }
    public double Min { get; private set; } = double.PositiveInfinity;
    public double Max { get; private set; } = double.NegativeInfinity;
    public double Mean { get; private set; }
    private double _m2;

    public void Add(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return;
        }

        Count++;
        if (value < Min) Min = value;
        if (value > Max) Max = value;

        var delta = value - Mean;
        Mean += delta / Count;
        var delta2 = value - Mean;
        _m2 += delta * delta2;
    }

    public void AddRange(IEnumerable<double> values)
    {
        foreach (var val in values)
        {
            Add(val);
        }
    }

    public double Variance => Count > 1 ? _m2 / (Count - 1) : 0.0;

    public double PopulationVariance => Count > 0 ? _m2 / Count : 0.0;

    public double StandardDeviation => Math.Sqrt(Variance);

    public double PopulationStandardDeviation => Math.Sqrt(PopulationVariance);

    public void Reset()
    {
        Count = 0;
        Min = double.PositiveInfinity;
        Max = double.NegativeInfinity;
        Mean = 0;
        _m2 = 0;
    }
}
