using MathNet.Numerics.LinearAlgebra;

namespace AutoVerify.Api.Analytics;

public static class GaussianMixtureEngine
{
    public static GaussianMixtureResult Compute(
        IReadOnlyList<(int RowIndex, double[] Values, ObservationReference? Observation)> rows,
        IReadOnlyList<string> featureNames,
        int requestedComponents = 3,
        string covarianceType = "diagonal",
        int maxIterations = 100,
        double tolerance = 1e-4,
        int? seed = null)
    {
        if (rows == null || rows.Count == 0)
        {
            throw new ArgumentException("Dataset is empty. At least 3 observations are required.");
        }

        var n = rows.Count;
        var p = featureNames.Count;
        if (p == 0)
        {
            throw new ArgumentException("At least 1 feature is required for GMM.");
        }

        var k = Math.Clamp(requestedComponents, 1, Math.Min(10, n));
        var maxIter = Math.Clamp(maxIterations, 10, 500);
        var tol = tolerance > 0 ? tolerance : 1e-4;
        var rngSeed = seed ?? 42;
        var rng = new Random(rngSeed);

        var requestedCovType = (covarianceType ?? "diagonal").Trim().ToLowerInvariant();
        var isFull = requestedCovType == "full";
        // Guard for full covariance: reject if high dimensions or insufficient samples
        if (isFull && (p > 10 || n < p * k + 5))
        {
            isFull = false;
            requestedCovType = "diagonal";
        }

        // Data array
        var xData = new double[n][];
        for (var i = 0; i < n; i++)
        {
            xData[i] = new double[p];
            var vals = rows[i].Values;
            for (var j = 0; j < p; j++)
            {
                xData[i][j] = j < vals.Length ? vals[j] : 0.0;
            }
        }

        // Compute overall feature variances to establish minimum variance floor (ridge)
        var globalVariances = new double[p];
        for (var j = 0; j < p; j++)
        {
            var acc = new WelfordAccumulator();
            for (var i = 0; i < n; i++) acc.Add(xData[i][j]);
            globalVariances[j] = acc.Variance > 1e-6 ? acc.Variance : 1.0;
        }

        // Initialization using quick K-means or random partitions
        var means = InitializeMeans(xData, k, rng);
        var weights = new double[k];
        for (var c = 0; c < k; c++) weights[c] = 1.0 / k;

        var variances = new double[k][];
        for (var c = 0; c < k; c++)
        {
            variances[c] = new double[p];
            for (var j = 0; j < p; j++)
            {
                variances[c][j] = Math.Max(1e-4, globalVariances[j]);
            }
        }

        var gamma = new double[n][];
        for (var i = 0; i < n; i++) gamma[i] = new double[k];

        double prevLogLikelihood = double.NegativeInfinity;
        var converged = false;
        var iter = 0;

        for (iter = 0; iter < maxIter; iter++)
        {
            // E-step with log-sum-exp
            var totalLogLikelihood = 0.0;
            for (var i = 0; i < n; i++)
            {
                var logComponents = new double[k];
                var maxLog = double.NegativeInfinity;

                for (var c = 0; c < k; c++)
                {
                    var logPi = Math.Log(Math.Max(1e-12, weights[c]));
                    var logN = ComputeLogGaussianDiag(xData[i], means[c], variances[c]);
                    var comp = logPi + logN;
                    logComponents[c] = comp;
                    if (comp > maxLog) maxLog = comp;
                }

                var sumExp = 0.0;
                for (var c = 0; c < k; c++)
                {
                    sumExp += Math.Exp(logComponents[c] - maxLog);
                }
                var logSumExp = maxLog + Math.Log(Math.Max(1e-12, sumExp));
                totalLogLikelihood += logSumExp;

                for (var c = 0; c < k; c++)
                {
                    gamma[i][c] = Math.Exp(logComponents[c] - logSumExp);
                }
            }

            // Check convergence
            if (iter > 0 && Math.Abs(totalLogLikelihood - prevLogLikelihood) < tol)
            {
                converged = true;
                prevLogLikelihood = totalLogLikelihood;
                break;
            }
            prevLogLikelihood = totalLogLikelihood;

            // M-step
            for (var c = 0; c < k; c++)
            {
                var nk = 0.0;
                for (var i = 0; i < n; i++) nk += gamma[i][c];

                weights[c] = Math.Max(1e-6, nk / n);

                for (var j = 0; j < p; j++)
                {
                    var meanSum = 0.0;
                    for (var i = 0; i < n; i++)
                    {
                        meanSum += gamma[i][c] * xData[i][j];
                    }
                    means[c][j] = meanSum / (nk + 1e-12);

                    var varSum = 0.0;
                    for (var i = 0; i < n; i++)
                    {
                        var diff = xData[i][j] - means[c][j];
                        varSum += gamma[i][c] * diff * diff;
                    }
                    var regularizedVar = (varSum / (nk + 1e-12)) + (1e-4 * globalVariances[j]);
                    variances[c][j] = Math.Max(1e-6, regularizedVar);
                }
            }

            // Normalize weights
            var sumW = weights.Sum();
            for (var c = 0; c < k; c++) weights[c] /= sumW;
        }

        // Full covariance matrices if requested
        double[][][]? fullCovariances = null;
        if (isFull)
        {
            fullCovariances = new double[k][][];
            for (var c = 0; c < k; c++)
            {
                var nk = 0.0;
                for (var i = 0; i < n; i++) nk += gamma[i][c];

                var cov = Matrix<double>.Build.Dense(p, p);
                for (var i = 0; i < n; i++)
                {
                    var g = gamma[i][c];
                    for (var r = 0; r < p; r++)
                    {
                        var diffR = xData[i][r] - means[c][r];
                        for (var col = 0; col < p; col++)
                        {
                            var diffC = xData[i][col] - means[c][col];
                            cov[r, col] += g * diffR * diffC;
                        }
                    }
                }
                cov /= (nk + 1e-12);
                for (var j = 0; j < p; j++)
                {
                    cov[j, j] += 1e-4 * globalVariances[j];
                }

                fullCovariances[c] = new double[p][];
                for (var r = 0; r < p; r++)
                {
                    fullCovariances[c][r] = new double[p];
                    for (var col = 0; col < p; col++)
                    {
                        fullCovariances[c][r][col] = Math.Round(cov[r, col], 4);
                    }
                }
            }
        }

        // Compute AIC & BIC
        // Num params for diagonal: K*p (means) + K*p (variances) + (K - 1) (weights)
        var numParams = isFull
            ? k * p + k * (p * (p + 1) / 2) + (k - 1)
            : 2 * k * p + (k - 1);
        var aic = 2 * numParams - 2 * prevLogLikelihood;
        var bic = numParams * Math.Log(n) - 2 * prevLogLikelihood;

        // Component summaries
        var summaries = new List<GmmComponentSummary>(k);
        for (var c = 0; c < k; c++)
        {
            var assignedCount = 0;
            for (var i = 0; i < n; i++)
            {
                var bestC = 0;
                var maxP = -1.0;
                for (var comp = 0; comp < k; comp++)
                {
                    if (gamma[i][comp] > maxP)
                    {
                        maxP = gamma[i][comp];
                        bestC = comp;
                    }
                }
                if (bestC == c) assignedCount++;
            }

            summaries.Add(new GmmComponentSummary(
                c,
                Math.Round(weights[c], 4),
                assignedCount,
                means[c].Select(m => Math.Round(m, 4)).ToArray(),
                variances[c].Select(v => Math.Round(v, 4)).ToArray()
            ));
        }

        // Point assignments
        var assignments = new List<GmmAssignment>(n);
        for (var i = 0; i < n; i++)
        {
            var bestC = 0;
            var maxProb = -1.0;
            var entropy = 0.0;
            for (var c = 0; c < k; c++)
            {
                var pVal = gamma[i][c];
                if (pVal > maxProb)
                {
                    maxProb = pVal;
                    bestC = c;
                }
                if (pVal > 1e-12)
                {
                    entropy -= pVal * Math.Log(pVal);
                }
            }

            var logDensity = ComputeLogGaussianDiag(xData[i], means[bestC], variances[bestC]);
            var obs = rows[i].Observation ?? new ObservationReference(Guid.Empty, rows[i].RowIndex, null, null, null, null, null, null, null);

            assignments.Add(new GmmAssignment(
                obs,
                bestC,
                Math.Round(maxProb, 4),
                gamma[i].Select(g => Math.Round(g, 4)).ToArray(),
                Math.Round(logDensity, 4),
                Math.Round(entropy, 4)
            ));
        }

        return new GaussianMixtureResult(
            k,
            requestedCovType,
            Math.Round(prevLogLikelihood, 4),
            Math.Round(aic, 4),
            Math.Round(bic, 4),
            converged,
            iter + 1,
            weights.Select(w => Math.Round(w, 4)).ToArray(),
            means.Select(m => m.Select(v => Math.Round(v, 4)).ToArray()).ToArray(),
            variances.Select(v => v.Select(val => Math.Round(val, 4)).ToArray()).ToArray(),
            fullCovariances,
            summaries,
            assignments,
            featureNames.ToList()
        );
    }

    private static double ComputeLogGaussianDiag(double[] x, double[] mean, double[] variance)
    {
        var p = x.Length;
        var sum = -0.5 * p * Math.Log(2 * Math.PI);
        for (var j = 0; j < p; j++)
        {
            var v = variance[j];
            var diff = x[j] - mean[j];
            sum += -0.5 * Math.Log(v) - (diff * diff) / (2.0 * v);
        }
        return sum;
    }

    private static double[][] InitializeMeans(double[][] data, int k, Random rng)
    {
        var n = data.Length;
        var p = data[0].Length;
        var means = new double[k][];

        // Seed K initial means evenly or randomly
        for (var c = 0; c < k; c++)
        {
            var idx = (int)((c + 0.5) * n / k);
            idx = Math.Clamp(idx, 0, n - 1);
            means[c] = (double[])data[idx].Clone();
            // Add tiny jitter to break ties
            for (var j = 0; j < p; j++)
            {
                means[c][j] += (rng.NextDouble() - 0.5) * 1e-3;
            }
        }

        return means;
    }
}
