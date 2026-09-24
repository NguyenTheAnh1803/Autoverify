using MathNet.Numerics.Distributions;
using MathNet.Numerics.LinearAlgebra;

namespace AutoVerify.Api.Analytics;

public sealed record DoeFactorEffect(
    string Term,
    string TermType, // "MainEffect", "Interaction", "Intercept"
    double Coefficient,
    double StandardError,
    double TStatistic,
    double PValue,
    double StandardizedEffectMagnitude,
    bool IsStatisticallySignificant
);

public sealed record DoeModelSummary(
    double R2,
    double AdjustedR2,
    double RootMeanSquareError,
    double SumOfSquaresModel,
    double SumOfSquaresResidual,
    double SumOfSquaresTotal,
    int DfModel,
    int DfResidual,
    int DfTotal,
    double? FStatistic,
    double? PValue
);

public sealed record DoeResidualPoint(
    int RowIndex,
    double Actual,
    double Fitted,
    double Residual
);

public sealed record DoeResult(
    string ResponseName,
    List<string> FactorNames,
    int TotalRuns,
    bool IsUnreplicatedSaturated,
    double SignificanceThreshold,
    DoeModelSummary Summary,
    List<DoeFactorEffect> EffectsPareto,
    List<DoeResidualPoint> ResidualDiagnostics,
    List<string> Warnings
);

public static class DoeEngine
{
    public static DoeResult Compute(
        IReadOnlyList<(int RowIndex, double Response, Dictionary<string, double> Factors)> data,
        string responseName,
        IReadOnlyList<string> factorNames,
        bool include2FactorInteractions = true,
        double alpha = 0.05)
    {
        var warnings = new List<string>();

        if (data == null || data.Count < 4)
        {
            throw new ArgumentException("At least 4 experimental runs are required for DOE analysis.");
        }

        if (factorNames == null || factorNames.Count < 1)
        {
            throw new ArgumentException("At least 1 factor is required for DOE analysis.");
        }

        var n = data.Count;
        var validFactors = new List<string>();

        // Verify each factor has at least 2 distinct levels
        foreach (var factor in factorNames)
        {
            var distinctLevels = data.Select(r => r.Factors.GetValueOrDefault(factor, 0.0)).Distinct().Count();
            if (distinctLevels >= 2)
            {
                validFactors.Add(factor);
            }
            else
            {
                warnings.Add($"Factor '{factor}' has only {distinctLevels} distinct level; excluded from model.");
            }
        }

        if (validFactors.Count == 0)
        {
            throw new ArgumentException("No factors with at least 2 distinct levels were found.");
        }

        // Build list of terms: Intercept + Main Effects + Interactions
        var terms = new List<(string Name, string Type, Func<Dictionary<string, double>, double> Evaluator)>();
        terms.Add(("Intercept", "Intercept", _ => 1.0));

        foreach (var f in validFactors)
        {
            var factorName = f;
            terms.Add((factorName, "MainEffect", row => row.GetValueOrDefault(factorName, 0.0)));
        }

        if (include2FactorInteractions && validFactors.Count >= 2)
        {
            for (var i = 0; i < validFactors.Count; i++)
            {
                for (var j = i + 1; j < validFactors.Count; j++)
                {
                    var f1 = validFactors[i];
                    var f2 = validFactors[j];
                    terms.Add(($"{f1}:{f2}", "Interaction", row => row.GetValueOrDefault(f1, 0.0) * row.GetValueOrDefault(f2, 0.0)));
                }
            }
        }

        // If saturated (more terms than observations - 1), prune interaction terms
        if (terms.Count >= n)
        {
            warnings.Add($"Total terms ({terms.Count}) exceed or equal degrees of freedom (N = {n}). Interaction terms pruned to prevent overparameterization.");
            terms = terms.Where(t => t.Type != "Interaction").ToList();
        }

        // If still saturated, prune to N - 1 terms
        if (terms.Count >= n)
        {
            terms = terms.Take(Math.Max(2, n - 1)).ToList();
        }

        var p = terms.Count;
        var xMatrix = Matrix<double>.Build.Dense(n, p);
        var yVector = Vector<double>.Build.Dense(n);

        for (var i = 0; i < n; i++)
        {
            yVector[i] = data[i].Response;
            var rowDict = data[i].Factors;
            for (var j = 0; j < p; j++)
            {
                xMatrix[i, j] = terms[j].Evaluator(rowDict);
            }
        }

        // Solve OLS: beta = (X^T X)^(-1) X^T Y via QR
        var qr = xMatrix.QR();
        var beta = qr.Solve(yVector);

        var yHat = xMatrix * beta;
        var residuals = yVector - yHat;

        var yBar = yVector.Average();
        var ssTotal = yVector.Sum(y => Math.Pow(y - yBar, 2));
        var ssResidual = residuals.Sum(e => e * e);
        var ssModel = Math.Max(0.0, ssTotal - ssResidual);

        var dfTotal = n - 1;
        var dfModel = p - 1;
        var dfResidual = n - p;

        var isSaturated = dfResidual <= 0;
        double rmse = 0.0;
        double r2 = ssTotal > 1e-12 ? Math.Max(0.0, 1.0 - (ssResidual / ssTotal)) : 1.0;
        double adjR2 = (ssTotal > 1e-12 && dfResidual > 0 && dfTotal > 0)
            ? Math.Max(0.0, 1.0 - ((ssResidual / dfResidual) / (ssTotal / dfTotal)))
            : r2;

        double? fStat = null;
        double? pVal = null;

        if (!isSaturated)
        {
            var msModel = dfModel > 0 ? ssModel / dfModel : 0.0;
            var msResidual = ssResidual / dfResidual;
            rmse = Math.Sqrt(msResidual);

            if (msResidual > 1e-12)
            {
                fStat = msModel / msResidual;
                try
                {
                    var fDist = new FisherSnedecor(dfModel, dfResidual);
                    pVal = Math.Max(0.0, 1.0 - fDist.CumulativeDistribution(fStat.Value));
                }
                catch { }
            }
        }
        else
        {
            warnings.Add("Model is saturated (dfResidual = 0). Standard errors estimated via Lenth's Pseudo Standard Error (PSE).");
        }

        // Standard errors for coefficients
        var effectsList = new List<DoeFactorEffect>(p);
        Matrix<double>? covMatrix = null;
        if (!isSaturated)
        {
            try
            {
                var xtxInv = (xMatrix.TransposeThisAndMultiply(xMatrix)).Inverse();
                covMatrix = xtxInv * (rmse * rmse);
            }
            catch
            {
                covMatrix = null;
            }
        }

        // Calculate Lenth's PSE if saturated
        var lenthPse = 1.0;
        if (isSaturated)
        {
            var nonIntercept = beta.Skip(1).Select(Math.Abs).ToList();
            if (nonIntercept.Count > 0)
            {
                var s0 = 1.5 * Median(nonIntercept);
                var filtered = nonIntercept.Where(e => e < 2.5 * s0).ToList();
                lenthPse = filtered.Count > 0 ? 1.5 * Median(filtered) : (s0 > 1e-12 ? s0 : 1.0);
            }
        }

        var criticalT = 1.96;
        if (!isSaturated && dfResidual > 0)
        {
            try
            {
                var tDist = new StudentT(0, 1, dfResidual);
                criticalT = Math.Abs(tDist.InverseCumulativeDistribution(1.0 - alpha / 2.0));
            }
            catch { }
        }

        for (var j = 0; j < p; j++)
        {
            var coef = beta[j];
            var term = terms[j];

            double se;
            double tStatTerm;
            double pValTerm;

            if (!isSaturated && covMatrix != null && covMatrix[j, j] >= 0)
            {
                se = Math.Sqrt(covMatrix[j, j]);
                tStatTerm = se > 1e-12 ? coef / se : 0.0;
                try
                {
                    var tDist = new StudentT(0, 1, dfResidual);
                    pValTerm = 2.0 * (1.0 - tDist.CumulativeDistribution(Math.Abs(tStatTerm)));
                }
                catch
                {
                    pValTerm = 1.0;
                }
            }
            else
            {
                se = lenthPse;
                tStatTerm = se > 1e-12 ? coef / se : 0.0;
                var effectiveDf = Math.Max(1, (p - 1) / 3);
                try
                {
                    var tDist = new StudentT(0, 1, effectiveDf);
                    pValTerm = 2.0 * (1.0 - tDist.CumulativeDistribution(Math.Abs(tStatTerm)));
                }
                catch
                {
                    pValTerm = 1.0;
                }
            }

            var isSig = Math.Abs(tStatTerm) >= criticalT;
            var effectMagnitude = se > 1e-12 ? Math.Abs(tStatTerm) : Math.Abs(coef);

            effectsList.Add(new DoeFactorEffect(
                term.Name,
                term.Type,
                coef,
                se,
                tStatTerm,
                Math.Clamp(pValTerm, 0.0, 1.0),
                effectMagnitude,
                isSig
            ));
        }

        var paretoRanked = effectsList
            .Where(e => e.TermType != "Intercept")
            .OrderByDescending(e => e.StandardizedEffectMagnitude)
            .ToList();

        var residualPoints = new List<DoeResidualPoint>(n);
        for (var i = 0; i < n; i++)
        {
            residualPoints.Add(new DoeResidualPoint(
                data[i].RowIndex,
                yVector[i],
                yHat[i],
                residuals[i]));
        }

        var summary = new DoeModelSummary(
            r2,
            adjR2,
            rmse,
            ssModel,
            ssResidual,
            ssTotal,
            dfModel,
            dfResidual,
            dfTotal,
            fStat,
            pVal
        );

        return new DoeResult(
            responseName,
            validFactors,
            n,
            isSaturated,
            criticalT,
            summary,
            paretoRanked,
            residualPoints,
            warnings
        );
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0.0;
        var sorted = values.OrderBy(x => x).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 != 0 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
