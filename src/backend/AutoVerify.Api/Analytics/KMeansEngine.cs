namespace AutoVerify.Api.Analytics;

public static class KMeansEngine
{
    public static KMeansResult Compute(
        IReadOnlyList<(int RowIndex, double[] Values, ObservationReference? Observation)> rows,
        IReadOnlyList<string> featureNames,
        int requestedK = 3,
        int maxIterations = 100,
        int nInit = 5,
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
            throw new ArgumentException("At least 1 feature is required for K-means.");
        }

        var k = Math.Clamp(requestedK, 2, Math.Min(20, n));
        var restarts = Math.Clamp(nInit, 1, 10);
        var maxIter = Math.Clamp(maxIterations, 10, 500);
        var rngSeed = seed ?? 42;

        // Compute means and standard deviations for standardization
        var means = new double[p];
        var stdDevs = new double[p];

        for (var j = 0; j < p; j++)
        {
            var acc = new WelfordAccumulator();
            for (var i = 0; i < n; i++)
            {
                var val = j < rows[i].Values.Length ? rows[i].Values[j] : 0.0;
                acc.Add(val);
            }
            means[j] = acc.Mean;
            stdDevs[j] = acc.StandardDeviation > 1e-12 ? acc.StandardDeviation : 1.0;
        }

        // Standardized data matrix
        var zData = new double[n][];
        for (var i = 0; i < n; i++)
        {
            zData[i] = new double[p];
            var vals = rows[i].Values;
            for (var j = 0; j < p; j++)
            {
                var v = j < vals.Length ? vals[j] : 0.0;
                zData[i][j] = (v - means[j]) / stdDevs[j];
            }
        }

        double bestInertia = double.PositiveInfinity;
        double[][] bestCentroids = Array.Empty<double[]>();
        int[] bestAssignments = Array.Empty<int>();
        int bestIterations = 0;
        bool bestConverged = false;

        for (var initIdx = 0; initIdx < restarts; initIdx++)
        {
            var runRng = new Random(rngSeed + initIdx * 10007);

            // K-means++ initialization
            var centroids = InitializeCentroidsKMeansPlusPlus(zData, k, runRng);
            var assignments = new int[n];
            var prevInertia = double.PositiveInfinity;
            var converged = false;
            var iter = 0;

            for (iter = 0; iter < maxIter; iter++)
            {
                var inertia = 0.0;

                // Assignment step
                for (var i = 0; i < n; i++)
                {
                    var minDistSq = double.PositiveInfinity;
                    var closestCluster = 0;
                    for (var c = 0; c < k; c++)
                    {
                        var distSq = DistanceSquared(zData[i], centroids[c]);
                        if (distSq < minDistSq)
                        {
                            minDistSq = distSq;
                            closestCluster = c;
                        }
                    }
                    assignments[i] = closestCluster;
                    inertia += minDistSq;
                }

                // Update step
                var newCentroids = new double[k][];
                var clusterCounts = new int[k];
                for (var c = 0; c < k; c++)
                {
                    newCentroids[c] = new double[p];
                }

                for (var i = 0; i < n; i++)
                {
                    var c = assignments[i];
                    clusterCounts[c]++;
                    for (var j = 0; j < p; j++)
                    {
                        newCentroids[c][j] += zData[i][j];
                    }
                }

                // Handle empty clusters by re-seeding with furthest sample
                for (var c = 0; c < k; c++)
                {
                    if (clusterCounts[c] == 0)
                    {
                        var furthestIdx = 0;
                        var maxDistSq = -1.0;
                        for (var i = 0; i < n; i++)
                        {
                            var d = DistanceSquared(zData[i], centroids[assignments[i]]);
                            if (d > maxDistSq)
                            {
                                maxDistSq = d;
                                furthestIdx = i;
                            }
                        }
                        Array.Copy(zData[furthestIdx], newCentroids[c], p);
                        clusterCounts[c] = 1;
                    }
                    else
                    {
                        for (var j = 0; j < p; j++)
                        {
                            newCentroids[c][j] /= clusterCounts[c];
                        }
                    }
                }

                // Check centroid shift convergence
                var maxShiftSq = 0.0;
                for (var c = 0; c < k; c++)
                {
                    var shift = DistanceSquared(centroids[c], newCentroids[c]);
                    if (shift > maxShiftSq) maxShiftSq = shift;
                }

                centroids = newCentroids;

                if (maxShiftSq < 1e-6 || Math.Abs(prevInertia - inertia) < 1e-5)
                {
                    converged = true;
                    break;
                }

                prevInertia = inertia;
            }

            // Final inertia calculation for this run
            var finalInertia = 0.0;
            for (var i = 0; i < n; i++)
            {
                finalInertia += DistanceSquared(zData[i], centroids[assignments[i]]);
            }

            if (finalInertia < bestInertia)
            {
                bestInertia = finalInertia;
                bestCentroids = centroids;
                bestAssignments = assignments;
                bestIterations = iter + 1;
                bestConverged = converged;
            }
        }

        // Generate cluster summaries
        var summaries = new List<KMeansClusterSummary>(k);
        var clusterSizes = new int[k];
        var clusterDistanceSums = new double[k];

        for (var i = 0; i < n; i++)
        {
            var c = bestAssignments[i];
            clusterSizes[c]++;
            clusterDistanceSums[c] += Math.Sqrt(DistanceSquared(zData[i], bestCentroids[c]));
        }

        for (var c = 0; c < k; c++)
        {
            var size = clusterSizes[c];
            var pct = Math.Round((double)size / n * 100.0, 2);
            var scaledCentroid = bestCentroids[c].Select(v => Math.Round(v, 4)).ToArray();
            var origCentroid = new double[p];
            for (var j = 0; j < p; j++)
            {
                origCentroid[j] = Math.Round(bestCentroids[c][j] * stdDevs[j] + means[j], 4);
            }
            var meanDist = size > 0 ? Math.Round(clusterDistanceSums[c] / size, 4) : 0.0;

            summaries.Add(new KMeansClusterSummary(
                c,
                size,
                pct,
                scaledCentroid,
                origCentroid,
                meanDist
            ));
        }

        // Generate point assignments
        var assignmentsList = new List<KMeansAssignment>(n);
        for (var i = 0; i < n; i++)
        {
            var c = bestAssignments[i];
            var dist = Math.Round(Math.Sqrt(DistanceSquared(zData[i], bestCentroids[c])), 4);
            var obs = rows[i].Observation ?? new ObservationReference(Guid.Empty, rows[i].RowIndex, null, null, null, null, null, null, null);
            assignmentsList.Add(new KMeansAssignment(obs, c, dist));
        }

        // Calculate silhouette score if n in [k+1, 2000]
        double? silhouetteScore = null;
        if (k > 1 && n > k && n <= 2000)
        {
            silhouetteScore = ComputeSilhouetteScore(zData, bestAssignments, k);
        }

        return new KMeansResult(
            k,
            Math.Round(bestInertia, 4),
            bestIterations,
            bestConverged,
            restarts,
            silhouetteScore.HasValue ? Math.Round(silhouetteScore.Value, 4) : null,
            summaries,
            assignmentsList,
            featureNames.ToList(),
            means.Select(m => Math.Round(m, 4)).ToArray(),
            stdDevs.Select(s => Math.Round(s, 4)).ToArray()
        );
    }

    private static double[][] InitializeCentroidsKMeansPlusPlus(double[][] data, int k, Random rng)
    {
        var n = data.Length;
        var p = data[0].Length;
        var centroids = new double[k][];

        // Pick first centroid uniformly at random
        var firstIdx = rng.Next(n);
        centroids[0] = (double[])data[firstIdx].Clone();

        var distSq = new double[n];
        for (var i = 0; i < n; i++)
        {
            distSq[i] = DistanceSquared(data[i], centroids[0]);
        }

        // Pick subsequent centroids proportional to distance squared
        for (var c = 1; c < k; c++)
        {
            var sumDist = 0.0;
            for (var i = 0; i < n; i++) sumDist += distSq[i];

            if (sumDist <= 1e-12)
            {
                var randIdx = rng.Next(n);
                centroids[c] = (double[])data[randIdx].Clone();
            }
            else
            {
                var target = rng.NextDouble() * sumDist;
                var cumulative = 0.0;
                var chosen = 0;
                for (var i = 0; i < n; i++)
                {
                    cumulative += distSq[i];
                    if (cumulative >= target)
                    {
                        chosen = i;
                        break;
                    }
                }
                centroids[c] = (double[])data[chosen].Clone();
            }

            // Update distance squared to closest centroid so far
            for (var i = 0; i < n; i++)
            {
                var d = DistanceSquared(data[i], centroids[c]);
                if (d < distSq[i]) distSq[i] = d;
            }
        }

        return centroids;
    }

    private static double DistanceSquared(double[] a, double[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            var diff = a[i] - b[i];
            sum += diff * diff;
        }
        return sum;
    }

    private static double ComputeSilhouetteScore(double[][] data, int[] assignments, int k)
    {
        var n = data.Length;
        var clusterCounts = new int[k];
        for (var i = 0; i < n; i++) clusterCounts[assignments[i]]++;

        var totalSilhouette = 0.0;
        var validPoints = 0;

        for (var i = 0; i < n; i++)
        {
            var ci = assignments[i];
            if (clusterCounts[ci] <= 1) continue; // single point cluster silhouette is 0 or undefined

            // Mean intra-cluster distance a(i)
            var intraSum = 0.0;
            for (var j = 0; j < n; j++)
            {
                if (i != j && assignments[j] == ci)
                {
                    intraSum += Math.Sqrt(DistanceSquared(data[i], data[j]));
                }
            }
            var ai = intraSum / (clusterCounts[ci] - 1);

            // Mean nearest-cluster distance b(i)
            var minInterDist = double.PositiveInfinity;
            for (var c = 0; c < k; c++)
            {
                if (c == ci || clusterCounts[c] == 0) continue;
                var interSum = 0.0;
                for (var j = 0; j < n; j++)
                {
                    if (assignments[j] == c)
                    {
                        interSum += Math.Sqrt(DistanceSquared(data[i], data[j]));
                    }
                }
                var meanInter = interSum / clusterCounts[c];
                if (meanInter < minInterDist) minInterDist = meanInter;
            }

            var bi = double.IsPositiveInfinity(minInterDist) ? 0.0 : minInterDist;
            var maxAb = Math.Max(ai, bi);
            var si = maxAb > 1e-12 ? (bi - ai) / maxAb : 0.0;

            totalSilhouette += si;
            validPoints++;
        }

        return validPoints > 0 ? totalSilhouette / validPoints : 0.0;
    }
}
