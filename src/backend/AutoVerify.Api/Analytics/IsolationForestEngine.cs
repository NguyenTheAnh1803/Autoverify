namespace AutoVerify.Api.Analytics;

public static class IsolationForestEngine
{
    private sealed class IsolationTreeNode
    {
        public bool IsLeaf { get; set; }
        public int Size { get; set; }
        public int SplitFeature { get; set; } = -1;
        public double SplitValue { get; set; }
        public IsolationTreeNode? Left { get; set; }
        public IsolationTreeNode? Right { get; set; }
    }

    public static IsolationForestResult Compute(
        IReadOnlyList<(int RowIndex, double[] Values, ObservationReference? Observation)> rows,
        IReadOnlyList<string> featureNames,
        int nTrees = 100,
        int? maxSamples = null,
        double contamination = 0.05,
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
            throw new ArgumentException("At least 1 feature is required for Isolation Forest.");
        }

        var numTrees = Math.Clamp(nTrees, 10, 200);
        var minSample = Math.Min(8, n);
        var subsampleSize = Math.Clamp(maxSamples ?? Math.Min(256, n), minSample, n);
        var contam = Math.Clamp(contamination, 0.001, 0.5);
        var rngSeed = seed ?? 42;
        var rng = new Random(rngSeed);

        var maxDepth = (int)Math.Ceiling(Math.Log2(Math.Max(2, subsampleSize)));
        var cSubsample = AveragePathLengthOfBst(subsampleSize);

        // Convert data to matrix
        var data = new double[n][];
        for (var i = 0; i < n; i++)
        {
            data[i] = new double[p];
            var vals = rows[i].Values;
            for (var j = 0; j < p; j++)
            {
                data[i][j] = j < vals.Length ? vals[j] : 0.0;
            }
        }

        // Build trees
        var trees = new IsolationTreeNode[numTrees];
        for (var t = 0; t < numTrees; t++)
        {
            // Sample without replacement (or with replacement if N < subsampleSize)
            var sampleIndices = SampleIndices(n, subsampleSize, rng);
            trees[t] = BuildTree(data, sampleIndices, 0, maxDepth, rng, p);
        }

        // Evaluate all observations
        var pathLengths = new double[n];
        var featureScores = new double[n][];
        for (var i = 0; i < n; i++) featureScores[i] = new double[p];

        for (var i = 0; i < n; i++)
        {
            var point = data[i];
            var totalPath = 0.0;

            for (var t = 0; t < numTrees; t++)
            {
                var (path, splits) = EvaluatePathAndContributions(trees[t], point, 0);
                totalPath += path;
                for (var j = 0; j < p; j++)
                {
                    featureScores[i][j] += splits[j];
                }
            }

            pathLengths[i] = totalPath / numTrees;
        }

        // Anomaly scores: s(x, n) = 2^(- E(h(x)) / c(n))
        var anomalyScores = new double[n];
        for (var i = 0; i < n; i++)
        {
            var exp = cSubsample > 1e-6 ? -pathLengths[i] / cSubsample : 0.0;
            anomalyScores[i] = Math.Pow(2.0, exp);
        }

        // Determine score threshold based on contamination
        var sortedScores = anomalyScores.OrderByDescending(s => s).ToArray();
        var anomalyCutoffIndex = Math.Min(n - 1, (int)Math.Ceiling(n * contam) - 1);
        var scoreThreshold = sortedScores[Math.Max(0, anomalyCutoffIndex)];

        // Assignments and contributions
        var assignments = new List<IsolationForestAssignment>(n);
        var globalFeatureSums = new double[p];
        var anomalyCount = 0;

        for (var i = 0; i < n; i++)
        {
            var score = Math.Round(anomalyScores[i], 4);
            var isAnomaly = anomalyScores[i] >= scoreThreshold;
            if (isAnomaly) anomalyCount++;

            var normPath = Math.Round(pathLengths[i] / cSubsample, 4);
            var obs = rows[i].Observation ?? new ObservationReference(Guid.Empty, rows[i].RowIndex, null, null, null, null, null, null, null);

            assignments.Add(new IsolationForestAssignment(
                obs,
                score,
                isAnomaly,
                normPath
            ));

            if (isAnomaly)
            {
                for (var j = 0; j < p; j++)
                {
                    globalFeatureSums[j] += featureScores[i][j];
                }
            }
        }

        // Global feature contributions
        var totalGlobalSum = globalFeatureSums.Sum();
        var globalContributions = new Dictionary<string, double>();
        for (var j = 0; j < p; j++)
        {
            var ratio = totalGlobalSum > 1e-12 ? globalFeatureSums[j] / totalGlobalSum : 1.0 / p;
            globalContributions[featureNames[j]] = Math.Round(ratio, 4);
        }

        // Top anomalies (up to 20)
        var topAnomalies = new List<TopAnomalyItem>();
        var indexedScores = anomalyScores.Select((s, idx) => (Score: s, Index: idx))
            .OrderByDescending(x => x.Score)
            .Take(Math.Min(20, n))
            .ToList();

        for (var rank = 0; rank < indexedScores.Count; rank++)
        {
            var item = indexedScores[rank];
            var i = item.Index;
            var obs = rows[i].Observation ?? new ObservationReference(Guid.Empty, rows[i].RowIndex, null, null, null, null, null, null, null);

            var featContrib = new Dictionary<string, double>();
            var rowSum = featureScores[i].Sum();
            for (var j = 0; j < p; j++)
            {
                var r = rowSum > 1e-12 ? featureScores[i][j] / rowSum : 1.0 / p;
                featContrib[featureNames[j]] = Math.Round(r, 4);
            }

            topAnomalies.Add(new TopAnomalyItem(
                obs,
                Math.Round(item.Score, 4),
                rank + 1,
                featContrib
            ));
        }

        return new IsolationForestResult(
            numTrees,
            subsampleSize,
            Math.Round(contam, 4),
            Math.Round(scoreThreshold, 4),
            n,
            anomalyCount,
            featureNames.ToList(),
            globalContributions,
            assignments,
            topAnomalies
        );
    }

    private static IsolationTreeNode BuildTree(
        double[][] data,
        int[] sampleIndices,
        int currentDepth,
        int maxDepth,
        Random rng,
        int numFeatures)
    {
        if (currentDepth >= maxDepth || sampleIndices.Length <= 1)
        {
            return new IsolationTreeNode { IsLeaf = true, Size = sampleIndices.Length };
        }

        // Find features with non-zero spread among samples
        var candidateFeatures = new List<(int FeatureIdx, double Min, double Max)>();
        for (var f = 0; f < numFeatures; f++)
        {
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            for (var i = 0; i < sampleIndices.Length; i++)
            {
                var val = data[sampleIndices[i]][f];
                if (val < min) min = val;
                if (val > max) max = val;
            }

            if (max - min > 1e-9)
            {
                candidateFeatures.Add((f, min, max));
            }
        }

        if (candidateFeatures.Count == 0)
        {
            return new IsolationTreeNode { IsLeaf = true, Size = sampleIndices.Length };
        }

        // Randomly pick one candidate feature
        var chosen = candidateFeatures[rng.Next(candidateFeatures.Count)];
        var splitVal = chosen.Min + rng.NextDouble() * (chosen.Max - chosen.Min);

        var leftIndices = new List<int>();
        var rightIndices = new List<int>();

        for (var i = 0; i < sampleIndices.Length; i++)
        {
            var idx = sampleIndices[i];
            if (data[idx][chosen.FeatureIdx] < splitVal)
            {
                leftIndices.Add(idx);
            }
            else
            {
                rightIndices.Add(idx);
            }
        }

        var node = new IsolationTreeNode
        {
            IsLeaf = false,
            Size = sampleIndices.Length,
            SplitFeature = chosen.FeatureIdx,
            SplitValue = splitVal,
            Left = BuildTree(data, leftIndices.ToArray(), currentDepth + 1, maxDepth, rng, numFeatures),
            Right = BuildTree(data, rightIndices.ToArray(), currentDepth + 1, maxDepth, rng, numFeatures)
        };

        return node;
    }

    private static (double PathLength, double[] SplitContributions) EvaluatePathAndContributions(
        IsolationTreeNode node,
        double[] point,
        int depth)
    {
        var splits = new double[point.Length];
        var curr = node;
        var currentDepth = depth;

        while (curr != null && !curr.IsLeaf)
        {
            // Attribution: shallow splits give higher weight to isolation
            var weight = 1.0 / (currentDepth + 1);
            splits[curr.SplitFeature] += weight;

            if (point[curr.SplitFeature] < curr.SplitValue)
            {
                curr = curr.Left;
            }
            else
            {
                curr = curr.Right;
            }
            currentDepth++;
        }

        var leafSize = curr?.Size ?? 1;
        var totalPath = currentDepth + AveragePathLengthOfBst(leafSize);
        return (totalPath, splits);
    }

    private static double AveragePathLengthOfBst(int n)
    {
        if (n <= 1) return 0.0;
        if (n == 2) return 1.0;
        // Euler-Mascheroni constant = 0.5772156649
        const double eulerMascheroni = 0.5772156649;
        return 2.0 * (Math.Log(n - 1) + eulerMascheroni) - (2.0 * (n - 1) / n);
    }

    private static int[] SampleIndices(int totalCount, int sampleCount, Random rng)
    {
        var result = new int[sampleCount];
        if (sampleCount >= totalCount)
        {
            for (var i = 0; i < totalCount; i++) result[i] = i;
            return result;
        }

        // Reservoir sampling or Fisher-Yates shuffle
        var pool = Enumerable.Range(0, totalCount).ToArray();
        for (var i = 0; i < sampleCount; i++)
        {
            var j = rng.Next(i, totalCount);
            (pool[i], pool[j]) = (pool[j], pool[i]);
            result[i] = pool[i];
        }

        return result;
    }
}
