using MathNet.Numerics.Distributions;

namespace AutoVerify.Api.Analytics;

public static class FeatureRelationshipEngine
{
    public static FeatureRelationshipResult Compute(
        IReadOnlyList<(int RowIndex, double?[] Values, ObservationReference? Observation)> rows,
        IReadOnlyList<string> featureNames,
        int maxScatterPoints = 500)
    {
        if (rows == null || rows.Count == 0)
        {
            throw new ArgumentException("Dataset is empty. At least 3 observations are required.");
        }

        if (featureNames == null || featureNames.Count < 2)
        {
            throw new ArgumentException("At least 2 features are required to compute feature relationships.");
        }

        var p = featureNames.Count;
        var totalObs = rows.Count;

        // Missing counts and ratios per feature
        var missingCounts = new Dictionary<string, int>();
        var missingRatios = new Dictionary<string, double>();
        for (var j = 0; j < p; j++)
        {
            var miss = 0;
            for (var r = 0; r < totalObs; r++)
            {
                var val = j < rows[r].Values.Length ? rows[r].Values[j] : null;
                if (!val.HasValue || double.IsNaN(val.Value) || double.IsInfinity(val.Value))
                {
                    miss++;
                }
            }
            missingCounts[featureNames[j]] = miss;
            missingRatios[featureNames[j]] = Math.Round((double)miss / totalObs, 4);
        }

        var pearsonMatrix = new double?[p][];
        var spearmanMatrix = new double?[p][];
        var overlapCounts = new int[p][];
        for (var i = 0; i < p; i++)
        {
            pearsonMatrix[i] = new double?[p];
            spearmanMatrix[i] = new double?[p];
            overlapCounts[i] = new int[p];
            pearsonMatrix[i][i] = 1.0;
            spearmanMatrix[i][i] = 1.0;
            overlapCounts[i][i] = totalObs - missingCounts[featureNames[i]];
        }

        var rankedPairs = new List<FeatureRelationshipPair>();
        var allScatterPoints = new List<RelationshipScatterPoint>();

        for (var i = 0; i < p; i++)
        {
            for (var j = i + 1; j < p; j++)
            {
                var f1 = featureNames[i];
                var f2 = featureNames[j];

                var validPoints = new List<(int RowIndex, double X, double Y, ObservationReference? Obs)>();
                for (var r = 0; r < totalObs; r++)
                {
                    var row = rows[r];
                    var v1 = i < row.Values.Length ? row.Values[i] : null;
                    var v2 = j < row.Values.Length ? row.Values[j] : null;

                    if (v1.HasValue && v2.HasValue &&
                        !double.IsNaN(v1.Value) && !double.IsInfinity(v1.Value) &&
                        !double.IsNaN(v2.Value) && !double.IsInfinity(v2.Value))
                    {
                        validPoints.Add((row.RowIndex, v1.Value, v2.Value, row.Observation));
                    }
                }

                var n = validPoints.Count;
                overlapCounts[i][j] = n;
                overlapCounts[j][i] = n;

                if (n < 3)
                {
                    pearsonMatrix[i][j] = null;
                    pearsonMatrix[j][i] = null;
                    spearmanMatrix[i][j] = null;
                    spearmanMatrix[j][i] = null;

                    rankedPairs.Add(new FeatureRelationshipPair(
                        f1,
                        f2,
                        null,
                        null,
                        n,
                        null,
                        "insufficient_data"
                    ));
                    continue;
                }

                // Compute Pearson
                var accX = new WelfordAccumulator();
                var accY = new WelfordAccumulator();
                for (var k = 0; k < n; k++)
                {
                    accX.Add(validPoints[k].X);
                    accY.Add(validPoints[k].Y);
                }

                var meanX = accX.Mean;
                var meanY = accY.Mean;
                var stdX = accX.StandardDeviation;
                var stdY = accY.StandardDeviation;

                double? pearson;
                double? pValue = null;

                if (stdX <= 1e-12 || stdY <= 1e-12)
                {
                    pearson = 0.0;
                    pValue = 1.0;
                }
                else
                {
                    double covSum = 0.0;
                    for (var k = 0; k < n; k++)
                    {
                        covSum += (validPoints[k].X - meanX) * (validPoints[k].Y - meanY);
                    }
                    var denom = (n - 1) * stdX * stdY;
                    var rVal = denom > 0 ? Math.Clamp(covSum / denom, -1.0, 1.0) : 0.0;
                    pearson = Math.Round(rVal, 4);

                    if (Math.Abs(rVal) >= 1.0)
                    {
                        pValue = 0.0;
                    }
                    else
                    {
                        var df = n - 2;
                        var t = Math.Abs(rVal) * Math.Sqrt(df / (1.0 - rVal * rVal));
                        var cdf = StudentT.CDF(0, 1, df, t);
                        pValue = Math.Round(Math.Clamp(2.0 * (1.0 - cdf), 0.0, 1.0), 6);
                    }
                }

                // Compute Spearman
                var xVals = new double[n];
                var yVals = new double[n];
                for (var k = 0; k < n; k++)
                {
                    xVals[k] = validPoints[k].X;
                    yVals[k] = validPoints[k].Y;
                }

                var xRanks = ComputeRanks(xVals);
                var yRanks = ComputeRanks(yVals);

                var accRx = new WelfordAccumulator();
                var accRy = new WelfordAccumulator();
                for (var k = 0; k < n; k++)
                {
                    accRx.Add(xRanks[k]);
                    accRy.Add(yRanks[k]);
                }

                double? spearman;
                if (accRx.StandardDeviation <= 1e-12 || accRy.StandardDeviation <= 1e-12)
                {
                    spearman = 0.0;
                }
                else
                {
                    double rankCov = 0.0;
                    for (var k = 0; k < n; k++)
                    {
                        rankCov += (xRanks[k] - accRx.Mean) * (yRanks[k] - accRy.Mean);
                    }
                    var rankDenom = (n - 1) * accRx.StandardDeviation * accRy.StandardDeviation;
                    var rho = rankDenom > 0 ? Math.Clamp(rankCov / rankDenom, -1.0, 1.0) : 0.0;
                    spearman = Math.Round(rho, 4);
                }

                pearsonMatrix[i][j] = pearson;
                pearsonMatrix[j][i] = pearson;
                spearmanMatrix[i][j] = spearman;
                spearmanMatrix[j][i] = spearman;

                var strength = GetCorrelationStrength(pearson);

                rankedPairs.Add(new FeatureRelationshipPair(
                    f1,
                    f2,
                    pearson,
                    spearman,
                    n,
                    pValue,
                    strength
                ));

                // If this is the highest correlation or first pair, collect scatter sample
                if (allScatterPoints.Count == 0 && validPoints.Count > 0)
                {
                    var targetCount = Math.Min(n, Math.Max(10, maxScatterPoints));
                    var step = (double)n / targetCount;
                    for (var k = 0; k < targetCount; k++)
                    {
                        var idx = Math.Min((int)(k * step), n - 1);
                        var pt = validPoints[idx];
                        var obs = pt.Obs ?? new ObservationReference(Guid.Empty, pt.RowIndex, null, null, null, null, null, null, null);
                        allScatterPoints.Add(new RelationshipScatterPoint(obs, pt.X, pt.Y));
                    }
                }
            }
        }

        var sortedPairs = rankedPairs.OrderByDescending(p => p.Pearson.HasValue ? Math.Abs(p.Pearson.Value) : -1.0).ToList();

        // If sorted pairs has a top pair and it wasn't the first, extract top pair scatter points
        if (sortedPairs.Count > 0 && sortedPairs[0].Pearson.HasValue)
        {
            var top = sortedPairs[0];
            var f1Idx = featureNames.ToList().IndexOf(top.FeatureA);
            var f2Idx = featureNames.ToList().IndexOf(top.FeatureB);
            if (f1Idx >= 0 && f2Idx >= 0)
            {
                var topValid = new List<(int RowIndex, double X, double Y, ObservationReference? Obs)>();
                for (var r = 0; r < totalObs; r++)
                {
                    var row = rows[r];
                    var v1 = f1Idx < row.Values.Length ? row.Values[f1Idx] : null;
                    var v2 = f2Idx < row.Values.Length ? row.Values[f2Idx] : null;
                    if (v1.HasValue && v2.HasValue &&
                        !double.IsNaN(v1.Value) && !double.IsInfinity(v1.Value) &&
                        !double.IsNaN(v2.Value) && !double.IsInfinity(v2.Value))
                    {
                        topValid.Add((row.RowIndex, v1.Value, v2.Value, row.Observation));
                    }
                }

                if (topValid.Count > 0)
                {
                    allScatterPoints.Clear();
                    var targetCount = Math.Min(topValid.Count, Math.Max(10, maxScatterPoints));
                    var step = (double)topValid.Count / targetCount;
                    for (var k = 0; k < targetCount; k++)
                    {
                        var idx = Math.Min((int)(k * step), topValid.Count - 1);
                        var pt = topValid[idx];
                        var obs = pt.Obs ?? new ObservationReference(Guid.Empty, pt.RowIndex, null, null, null, null, null, null, null);
                        allScatterPoints.Add(new RelationshipScatterPoint(obs, pt.X, pt.Y));
                    }
                }
            }
        }

        var includedObs = rows.Count(r => r.Values.Any(v => v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value)));

        return new FeatureRelationshipResult(
            featureNames.ToList(),
            pearsonMatrix,
            spearmanMatrix,
            overlapCounts,
            missingCounts,
            missingRatios,
            sortedPairs,
            allScatterPoints,
            totalObs,
            includedObs
        );
    }

    private static string GetCorrelationStrength(double? r)
    {
        if (!r.HasValue) return "none";
        var abs = Math.Abs(r.Value);
        if (abs >= 0.8) return "very_strong";
        if (abs >= 0.6) return "strong";
        if (abs >= 0.4) return "moderate";
        if (abs >= 0.2) return "weak";
        return "very_weak";
    }

    private static double[] ComputeRanks(double[] values)
    {
        var n = values.Length;
        var indexed = new (double Value, int OriginalIndex)[n];
        for (var i = 0; i < n; i++)
        {
            indexed[i] = (values[i], i);
        }

        Array.Sort(indexed, (a, b) => a.Value.CompareTo(b.Value));

        var ranks = new double[n];
        var iStart = 0;
        while (iStart < n)
        {
            var iEnd = iStart;
            while (iEnd + 1 < n && Math.Abs(indexed[iEnd + 1].Value - indexed[iStart].Value) < 1e-12)
            {
                iEnd++;
            }

            var avgRank = 1.0 + (iStart + iEnd) / 2.0;
            for (var k = iStart; k <= iEnd; k++)
            {
                ranks[indexed[k].OriginalIndex] = avgRank;
            }

            iStart = iEnd + 1;
        }

        return ranks;
    }
}
