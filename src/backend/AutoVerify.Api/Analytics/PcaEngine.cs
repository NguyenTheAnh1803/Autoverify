using MathNet.Numerics.Distributions;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace AutoVerify.Api.Analytics;

public sealed record PcaPoint(
    int RowIndex,
    double Pc1,
    double Pc2,
    double HotellingT2,
    double SpeQ,
    bool IsHotellingOutlier,
    bool IsSpeOutlier,
    ObservationReference? Observation = null
);

public sealed record PcaComponentSummary(
    int ComponentIndex,
    string Name,
    double Eigenvalue,
    double ExplainedVarianceRatio,
    double CumulativeVarianceRatio,
    Dictionary<string, double> FeatureLoadings
);

public sealed record PcaResult(
    List<string> FeatureNames,
    int TotalSamples,
    int RetainedComponents,
    double ConfidenceLevel,
    double HotellingT2Threshold,
    double SpeQThreshold,
    List<PcaComponentSummary> Components,
    List<PcaPoint> SampledPoints,
    int TotalHotellingOutliers,
    int TotalSpeOutliers,
    List<string> Warnings
);

public static class PcaEngine
{
    public static PcaResult Compute(
        IReadOnlyList<(int RowIndex, double[] Values)> rows,
        IReadOnlyList<string> featureNames,
        int? requestedComponents = null,
        double confidenceLevel = 0.95,
        int maxSampledPoints = 1000)
    {
        var warnings = new List<string>();

        if (rows == null || rows.Count < 3)
        {
            throw new ArgumentException("At least 3 samples are required for PCA computation.");
        }

        var p = featureNames.Count;
        if (p < 2)
        {
            throw new ArgumentException("At least 2 features are required for PCA computation.");
        }

        var n = rows.Count;
        var validComponents = Math.Min(p, n - 1);
        var k = requestedComponents ?? Math.Min(validComponents, 2);
        if (k < 1) k = 1;
        if (k > validComponents) k = validComponents;

        var alpha = confidenceLevel is > 0.5 and < 0.9999 ? confidenceLevel : 0.95;

        // Step 1: Standardize data (Z-score)
        var rawMatrix = Matrix<double>.Build.Dense(n, p);
        for (var i = 0; i < n; i++)
        {
            var vals = rows[i].Values;
            for (var j = 0; j < p; j++)
            {
                rawMatrix[i, j] = j < vals.Length ? vals[j] : 0.0;
            }
        }

        var means = new double[p];
        var stdDevs = new double[p];
        for (var j = 0; j < p; j++)
        {
            var acc = new WelfordAccumulator();
            for (var i = 0; i < n; i++)
            {
                acc.Add(rawMatrix[i, j]);
            }
            means[j] = acc.Mean;
            stdDevs[j] = acc.StandardDeviation > 1e-12 ? acc.StandardDeviation : 1.0;
            if (acc.StandardDeviation <= 1e-12)
            {
                warnings.Add($"Feature '{featureNames[j]}' has near-zero variance; standardized to 0.");
            }
        }

        var zMatrix = Matrix<double>.Build.Dense(n, p);
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < p; j++)
            {
                zMatrix[i, j] = (rawMatrix[i, j] - means[j]) / stdDevs[j];
            }
        }

        // Step 2: Correlation matrix R = (1/(n-1)) * Z^T * Z
        var covMatrix = (zMatrix.TransposeThisAndMultiply(zMatrix)) / (n - 1);

        // Step 3: Eigenvalue decomposition
        var evd = covMatrix.Evd(Symmetricity.Symmetric);
        var rawEigenvalues = evd.EigenValues.Real();
        var rawEigenvectors = evd.EigenVectors;

        // Sort descending by eigenvalue
        var sortedPairs = rawEigenvalues
            .Select((lambda, idx) => (Lambda: Math.Max(0.0, lambda), Vector: rawEigenvectors.Column(idx), OriginalIdx: idx))
            .OrderByDescending(x => x.Lambda)
            .ToList();

        var totalVariance = sortedPairs.Sum(x => x.Lambda);
        if (totalVariance <= 1e-12) totalVariance = 1.0;

        var componentsSummary = new List<PcaComponentSummary>(k);
        var cumVar = 0.0;

        for (var a = 0; a < k; a++)
        {
            var lam = sortedPairs[a].Lambda;
            var ratio = lam / totalVariance;
            cumVar += ratio;

            var loadingsMap = new Dictionary<string, double>(p);
            var vec = sortedPairs[a].Vector;
            for (var j = 0; j < p; j++)
            {
                loadingsMap[featureNames[j]] = vec[j];
            }

            componentsSummary.Add(new PcaComponentSummary(
                a + 1,
                $"PC{a + 1}",
                lam,
                ratio,
                cumVar,
                loadingsMap));
        }

        // Step 4: Retained loadings matrix V_k (p x k)
        var vK = Matrix<double>.Build.Dense(p, k);
        for (var a = 0; a < k; a++)
        {
            var vec = sortedPairs[a].Vector;
            for (var j = 0; j < p; j++)
            {
                vK[j, a] = vec[j];
            }
        }

        // Scores T = Z * V_k (n x k)
        var scores = zMatrix * vK;

        // Step 5: Hotelling's T^2 threshold
        // T^2_crit = [k(n-1)/(n-k)] * F_alpha(k, n-k)
        double t2Threshold;
        try
        {
            var fDist = new FisherSnedecor(k, n - k);
            var fCrit = fDist.InverseCumulativeDistribution(alpha);
            t2Threshold = ((double)k * (n - 1) / (n - k)) * fCrit;
        }
        catch
        {
            // Fallback: Chi-squared with k degrees of freedom
            var chi = new ChiSquared(k);
            t2Threshold = chi.InverseCumulativeDistribution(alpha);
        }

        // Step 6: SPE / Q-Residual threshold via Jackson-Mudholkar
        // Residuals E = Z - T * V_k^T
        var reconstructed = scores * vK.Transpose();
        var residuals = zMatrix - reconstructed;

        var remainingLambdas = sortedPairs.Skip(k).Select(x => x.Lambda).ToList();
        var theta1 = remainingLambdas.Sum();
        var theta2 = remainingLambdas.Sum(x => x * x);
        var theta3 = remainingLambdas.Sum(x => x * x * x);

        double qThreshold;
        if (remainingLambdas.Count > 0 && theta1 > 1e-12 && theta2 > 1e-12)
        {
            var h0 = 1.0 - (2.0 * theta1 * theta3) / (3.0 * theta2 * theta2);
            var normal = new Normal(0, 1);
            var zAlpha = normal.InverseCumulativeDistribution(alpha);

            if (h0 > 0.001)
            {
                var term = (zAlpha * Math.Sqrt(2.0 * theta2 * h0 * h0) / theta1)
                    + 1.0
                    + (theta2 * h0 * (h0 - 1.0) / (theta1 * theta1));
                qThreshold = theta1 * Math.Pow(Math.Max(0.0, term), 1.0 / h0);
            }
            else
            {
                qThreshold = theta1 + zAlpha * Math.Sqrt(2.0 * theta2);
            }
        }
        else
        {
            // If all variance captured or p == k
            qThreshold = 0.001;
        }

        // Step 7: Calculate point metrics & detect outliers
        var allPoints = new List<PcaPoint>(n);
        var t2Outliers = 0;
        var speOutliers = 0;

        for (var i = 0; i < n; i++)
        {
            var pc1 = scores[i, 0];
            var pc2 = k > 1 ? scores[i, 1] : 0.0;

            // Hotelling T^2 = sum(t_ia^2 / lambda_a)
            var t2 = 0.0;
            for (var a = 0; a < k; a++)
            {
                var lam = sortedPairs[a].Lambda;
                if (lam > 1e-12)
                {
                    var sc = scores[i, a];
                    t2 += (sc * sc) / lam;
                }
            }

            // SPE Q = sum(e_ij^2)
            var q = 0.0;
            for (var j = 0; j < p; j++)
            {
                var e = residuals[i, j];
                q += e * e;
            }

            var isT2Outlier = t2 > t2Threshold;
            var isSpeOutlier = q > qThreshold;

            if (isT2Outlier) t2Outliers++;
            if (isSpeOutlier) speOutliers++;

            allPoints.Add(new PcaPoint(
                rows[i].RowIndex,
                pc1,
                pc2,
                t2,
                q,
                isT2Outlier,
                isSpeOutlier));
        }

        // If high cardinality, sample while retaining all outliers
        List<PcaPoint> finalPoints;
        if (n <= maxSampledPoints)
        {
            finalPoints = allPoints;
        }
        else
        {
            // Keep all outliers + evenly downsample remaining
            var outliers = allPoints.Where(pt => pt.IsHotellingOutlier || pt.IsSpeOutlier).ToList();
            var nonOutliers = allPoints.Where(pt => !pt.IsHotellingOutlier && !pt.IsSpeOutlier).ToList();

            var quotaForNormal = Math.Max(10, maxSampledPoints - outliers.Count);
            var step = Math.Max(1, nonOutliers.Count / quotaForNormal);

            var sampledNormal = nonOutliers
                .Where((_, idx) => idx % step == 0)
                .Take(quotaForNormal)
                .ToList();

            finalPoints = outliers.Concat(sampledNormal).OrderBy(x => x.RowIndex).ToList();
        }

        return new PcaResult(
            featureNames.ToList(),
            n,
            k,
            alpha,
            t2Threshold,
            qThreshold,
            componentsSummary,
            finalPoints,
            t2Outliers,
            speOutliers,
            warnings
        );
    }
}
