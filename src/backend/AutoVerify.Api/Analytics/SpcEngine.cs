namespace AutoVerify.Api.Analytics;

public sealed record SpcPoint(
    int RowIndex,
    double Value,
    string? SubgroupId = null,
    double? MovingRange = null,
    List<int>? ViolatingRuleIds = null,
    ObservationReference? Observation = null
);

public sealed record SpcChartSeries(
    string Name,
    double CenterLine,
    double UpperControlLimit,
    double LowerControlLimit,
    double Sigma,
    List<SpcPoint> Points
);

public sealed record ProcessCapabilityResult(
    double? Cp,
    double? Cpk,
    double? Pp,
    double? Ppk,
    double? Cpu,
    double? Cpl,
    double? Ppu,
    double? Ppl,
    double? Lsl,
    double? Usl,
    double? Target,
    double SigmaWithin,
    double SigmaOverall,
    bool IsValid,
    string? WarningMessage = null
);

public sealed record SpcResult(
    string ChartType,
    string FeatureName,
    int TotalSamples,
    int SubgroupCount,
    SpcChartSeries PrimaryChart, // Individual or X-bar
    SpcChartSeries? SecondaryChart, // Moving Range or R/S
    ProcessCapabilityResult? Capability,
    List<NelsonRuleViolation> Violations,
    List<string> Warnings
);

public static class SpcEngine
{
    public static SpcResult Compute(
        IReadOnlyList<(int RowIndex, double Value, string? SubgroupId)> inputData,
        string featureName,
        string chartType = "IMR",
        double? lsl = null,
        double? usl = null,
        double? target = null,
        bool isNgOnly = false)
    {
        return Compute(
            inputData.Select(x => (x.RowIndex, x.Value, x.SubgroupId, (ObservationReference?)null)).ToList(),
            featureName,
            chartType,
            lsl,
            usl,
            target,
            isNgOnly
        );
    }

    public static SpcResult Compute(
        IReadOnlyList<(int RowIndex, double Value, string? SubgroupId, ObservationReference? Observation)> inputData,
        string featureName,
        string chartType = "IMR",
        double? lsl = null,
        double? usl = null,
        double? target = null,
        bool isNgOnly = false)
    {
        var warnings = new List<string>();

        if (inputData == null || inputData.Count < 2)
        {
            throw new ArgumentException("At least 2 data points are required for SPC computation.");
        }

        if (isNgOnly)
        {
            warnings.Add("NG-Only Distribution Guard: This dataset contains only NG items. Tail truncation violates normality assumptions; control limits reflect defect dispersion rather than process steady-state.");
        }

        var normalizedChartType = chartType.ToUpperInvariant();
        if (normalizedChartType is "XBAR_R" or "XBARR") normalizedChartType = "XbarR";
        else if (normalizedChartType is "XBAR_S" or "XBARS") normalizedChartType = "XbarS";
        else normalizedChartType = "IMR";

        // If subgrouping requested, check if valid SubgroupId is provided
        var hasSubgroups = inputData.Any(x => !string.IsNullOrWhiteSpace(x.SubgroupId));
        if (!hasSubgroups && normalizedChartType != "IMR")
        {
            warnings.Add($"No subgroup IDs provided. Falling back from {normalizedChartType} to Individual Moving Range (I-MR).");
            normalizedChartType = "IMR";
        }

        if (normalizedChartType == "IMR")
        {
            return ComputeImr(inputData, featureName, lsl, usl, target, isNgOnly, warnings);
        }

        return ComputeSubgroupSpc(inputData, featureName, normalizedChartType, lsl, usl, target, isNgOnly, warnings);
    }

    private static SpcResult ComputeImr(
        IReadOnlyList<(int RowIndex, double Value, string? SubgroupId, ObservationReference? Observation)> inputData,
        string featureName,
        double? lsl,
        double? usl,
        double? target,
        bool isNgOnly,
        List<string> warnings)
    {
        var n = inputData.Count;
        var accumulator = new WelfordAccumulator();
        var movingRanges = new double[n];
        var mrAccumulator = new WelfordAccumulator();

        for (var i = 0; i < n; i++)
        {
            var val = inputData[i].Value;
            accumulator.Add(val);

            if (i > 0)
            {
                var mr = Math.Abs(val - inputData[i - 1].Value);
                movingRanges[i] = mr;
                mrAccumulator.Add(mr);
            }
        }

        var meanX = accumulator.Mean;
        var meanMr = mrAccumulator.Mean;
        var d2 = SpcConstants.GetD2(2); // 1.128
        var d4 = SpcConstants.GetD4(2); // 3.267
        var d3 = SpcConstants.GetD3(2); // 0.000

        var sigmaWithin = meanMr / d2;
        var sigmaOverall = accumulator.StandardDeviation;

        var uclX = meanX + 3.0 * sigmaWithin;
        var lclX = meanX - 3.0 * sigmaWithin;

        var uclMr = d4 * meanMr;
        var lclMr = d3 * meanMr;

        var valuesOnly = inputData.Select(x => x.Value).ToArray();
        var violations = NelsonRulesEngine.Evaluate(valuesOnly, meanX, sigmaWithin);
        var violationLookup = violations
            .GroupBy(v => v.Index)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RuleId).Distinct().ToList());

        var xPoints = new List<SpcPoint>(n);
        var mrPoints = new List<SpcPoint>(n);

        for (var i = 0; i < n; i++)
        {
            violationLookup.TryGetValue(i, out var pointRuleViolations);
            var mrVal = i == 0 ? (double?)null : movingRanges[i];

            xPoints.Add(new SpcPoint(
                inputData[i].RowIndex,
                inputData[i].Value,
                inputData[i].SubgroupId,
                mrVal,
                pointRuleViolations,
                inputData[i].Observation));

            if (i > 0)
            {
                mrPoints.Add(new SpcPoint(
                    inputData[i].RowIndex,
                    movingRanges[i],
                    inputData[i].SubgroupId,
                    movingRanges[i],
                    null,
                    inputData[i].Observation));
            }
        }

        var primarySeries = new SpcChartSeries("Individual (X)", meanX, uclX, lclX, sigmaWithin, xPoints);
        var secondarySeries = new SpcChartSeries("Moving Range (MR)", meanMr, uclMr, lclMr, meanMr, mrPoints);

        var capability = ComputeCapability(meanX, sigmaWithin, sigmaOverall, lsl, usl, target, isNgOnly);

        return new SpcResult(
            "IMR",
            featureName,
            n,
            1,
            primarySeries,
            secondarySeries,
            capability,
            violations,
            warnings
        );
    }

    private static SpcResult ComputeSubgroupSpc(
        IReadOnlyList<(int RowIndex, double Value, string? SubgroupId, ObservationReference? Observation)> inputData,
        string featureName,
        string chartType,
        double? lsl,
        double? usl,
        double? target,
        bool isNgOnly,
        List<string> warnings)
    {
        var grouped = inputData
            .GroupBy(x => x.SubgroupId ?? "G0")
            .Where(g => g.Count() >= 2)
            .ToList();

        if (grouped.Count < 2)
        {
            warnings.Add("Fewer than 2 valid subgroups of size >= 2 found. Falling back to I-MR.");
            return ComputeImr(inputData, featureName, lsl, usl, target, isNgOnly, warnings);
        }

        var subgroupMeans = new List<double>(grouped.Count);
        var subgroupRanges = new List<double>(grouped.Count);
        var subgroupStdDevs = new List<double>(grouped.Count);
        var subgroupSizes = new List<int>(grouped.Count);

        var allAccumulator = new WelfordAccumulator();

        foreach (var group in grouped)
        {
            var acc = new WelfordAccumulator();
            var vals = group.Select(x => x.Value).ToList();
            acc.AddRange(vals);

            subgroupMeans.Add(acc.Mean);
            subgroupRanges.Add(acc.Max - acc.Min);
            subgroupStdDevs.Add(acc.StandardDeviation);
            subgroupSizes.Add(vals.Count);

            allAccumulator.AddRange(vals);
        }

        var avgN = (int)Math.Round(subgroupSizes.Average());
        if (avgN < 2) avgN = 2;

        var grandMean = subgroupMeans.Average();
        var isSChart = chartType == "XbarS";

        double meanDispersion;
        double sigmaWithin;
        double uclX;
        double lclX;
        double uclDisp;
        double lclDisp;

        if (isSChart)
        {
            meanDispersion = subgroupStdDevs.Average();
            var c4 = SpcConstants.GetC4(avgN);
            var a3 = SpcConstants.GetA3(avgN);
            var b3 = SpcConstants.GetB3(avgN);
            var b4 = SpcConstants.GetB4(avgN);

            sigmaWithin = meanDispersion / c4;
            uclX = grandMean + a3 * meanDispersion;
            lclX = grandMean - a3 * meanDispersion;
            uclDisp = b4 * meanDispersion;
            lclDisp = b3 * meanDispersion;
        }
        else
        {
            meanDispersion = subgroupRanges.Average();
            var d2 = SpcConstants.GetD2(avgN);
            var a2 = SpcConstants.GetA2(avgN);
            var d3 = SpcConstants.GetD3(avgN);
            var d4 = SpcConstants.GetD4(avgN);

            sigmaWithin = meanDispersion / d2;
            uclX = grandMean + a2 * meanDispersion;
            lclX = grandMean - a2 * meanDispersion;
            uclDisp = d4 * meanDispersion;
            lclDisp = d3 * meanDispersion;
        }

        var sigmaMean = sigmaWithin / Math.Sqrt(avgN);
        var violations = NelsonRulesEngine.Evaluate(subgroupMeans, grandMean, sigmaMean);
        var violationLookup = violations
            .GroupBy(v => v.Index)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RuleId).Distinct().ToList());

        var xPoints = new List<SpcPoint>(grouped.Count);
        var dispPoints = new List<SpcPoint>(grouped.Count);

        for (var i = 0; i < grouped.Count; i++)
        {
            violationLookup.TryGetValue(i, out var ptRules);
            var firstRowIdx = grouped[i].First().RowIndex;
            var grpId = grouped[i].Key;

            xPoints.Add(new SpcPoint(firstRowIdx, subgroupMeans[i], grpId, null, ptRules));
            dispPoints.Add(new SpcPoint(firstRowIdx, isSChart ? subgroupStdDevs[i] : subgroupRanges[i], grpId));
        }

        var primaryChart = new SpcChartSeries($"X-bar ({chartType})", grandMean, uclX, lclX, sigmaMean, xPoints);
        var secondaryChart = new SpcChartSeries(isSChart ? "S Chart" : "R Chart", meanDispersion, uclDisp, lclDisp, meanDispersion, dispPoints);

        var capability = ComputeCapability(grandMean, sigmaWithin, allAccumulator.StandardDeviation, lsl, usl, target, isNgOnly);

        return new SpcResult(
            chartType,
            featureName,
            allAccumulator.Count > int.MaxValue ? int.MaxValue : (int)allAccumulator.Count,
            grouped.Count,
            primaryChart,
            secondaryChart,
            capability,
            violations,
            warnings
        );
    }

    private static ProcessCapabilityResult? ComputeCapability(
        double mean,
        double sigmaWithin,
        double sigmaOverall,
        double? lsl,
        double? usl,
        double? target,
        bool isNgOnly)
    {
        if (!lsl.HasValue && !usl.HasValue)
        {
            return null;
        }

        if (sigmaWithin <= 0 || sigmaOverall <= 0)
        {
            return null;
        }

        double? cp = null;
        double? pp = null;
        double? cpu = null;
        double? cpl = null;
        double? ppu = null;
        double? ppl = null;

        if (usl.HasValue && lsl.HasValue)
        {
            var tol = usl.Value - lsl.Value;
            if (tol > 0)
            {
                cp = tol / (6.0 * sigmaWithin);
                pp = tol / (6.0 * sigmaOverall);
            }
        }

        if (usl.HasValue)
        {
            cpu = (usl.Value - mean) / (3.0 * sigmaWithin);
            ppu = (usl.Value - mean) / (3.0 * sigmaOverall);
        }

        if (lsl.HasValue)
        {
            cpl = (mean - lsl.Value) / (3.0 * sigmaWithin);
            ppl = (mean - lsl.Value) / (3.0 * sigmaOverall);
        }

        double? cpk = null;
        if (cpu.HasValue && cpl.HasValue) cpk = Math.Min(cpu.Value, cpl.Value);
        else if (cpu.HasValue) cpk = cpu.Value;
        else if (cpl.HasValue) cpk = cpl.Value;

        double? ppk = null;
        if (ppu.HasValue && ppl.HasValue) ppk = Math.Min(ppu.Value, ppl.Value);
        else if (ppu.HasValue) ppk = ppu.Value;
        else if (ppl.HasValue) ppk = ppl.Value;

        var isValid = !isNgOnly;
        string? warn = isNgOnly
            ? "Capability metrics on NG-only dataset are statistically invalid due to censored tail distribution."
            : null;

        return new ProcessCapabilityResult(
            cp, cpk, pp, ppk, cpu, cpl, ppu, ppl,
            lsl, usl, target, sigmaWithin, sigmaOverall, isValid, warn);
    }
}
