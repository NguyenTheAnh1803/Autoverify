using System.Globalization;
using System.Text.RegularExpressions;
using AutoVerify.Api.Analytics.Contracts;

namespace AutoVerify.Api.Analytics;

/// <summary>
/// Core mathematical and algorithmic engine for analyzing Overkill data,
/// calculating Gold Thresholds, generating optimized scripts, and running What-If simulations.
/// </summary>
public static class RuleTuningEngine
{
    public sealed record FeatureSample(
        string KeyId,
        string DefectName,
        bool IsGroundTruthRealDefect,
        Dictionary<string, double> Features,
        string? LineCode = null,
        string? MachineCode = null,
        DateTimeOffset? InspectedAtUtc = null
    );

    public sealed record FeatureSeparationResult(
        string FeatureName,
        double? CurrentThresholdInScript,
        double ProposedThreshold,
        string ComparisonOperator, // ">" or "<"
        double OverkillMin,
        double OverkillMax,
        double OverkillMean,
        double RealMin,
        double RealMax,
        double RealMean,
        string SeparationKind, // "CompletelySeparated", "SignificantShift", "Overlapped"
        double ProjectedOverkillCutPercent,
        string RecommendedChangeCode,
        string Explanation
    );

    /// <summary>
    /// Analyzes Overkill vs RealDefect feature distributions and generates optimal threshold recommendations.
    /// </summary>
    public static List<FeatureSeparationResult> AnalyzeFeatureDistributions(
        IReadOnlyList<FeatureSample> samples,
        string? scriptContent = null)
    {
        var overkillSamples = samples.Where(s => !s.IsGroundTruthRealDefect).ToList();
        var realSamples = samples.Where(s => s.IsGroundTruthRealDefect).ToList();

        if (overkillSamples.Count == 0 || realSamples.Count == 0)
        {
            return [];
        }

        var allFeatures = overkillSamples
            .Concat(realSamples)
            .SelectMany(s => s.Features.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f)
            .ToList();

        var results = new List<FeatureSeparationResult>();

        foreach (var feat in allFeatures)
        {
            var oVals = overkillSamples
                .Where(s => s.Features.ContainsKey(feat))
                .Select(s => s.Features[feat])
                .ToList();

            var rVals = realSamples
                .Where(s => s.Features.ContainsKey(feat))
                .Select(s => s.Features[feat])
                .ToList();

            if (oVals.Count == 0 || rVals.Count == 0) continue;

            var oMin = Math.Round(oVals.Min(), 2);
            var oMax = Math.Round(oVals.Max(), 2);
            var oMean = Math.Round(oVals.Average(), 2);

            var rMin = Math.Round(rVals.Min(), 2);
            var rMax = Math.Round(rVals.Max(), 2);
            var rMean = Math.Round(rVals.Average(), 2);

            // Detect existing threshold in script if available
            double? currentThreshold = null;
            string compOp = ">";
            if (!string.IsNullOrWhiteSpace(scriptContent))
            {
                var match = Regex.Match(scriptContent, $@"\b{Regex.Escape(feat)}\s*(>|<|>=|<=|==)\s*([\d.]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    compOp = match.Groups[1].Value.Trim();
                    if (double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ct))
                    {
                        currentThreshold = ct;
                    }
                }
            }

            // Determine if higher values mean defect (e.g. Area, Width, Confidence)
            // or lower values mean defect (e.g. GrayMin)
            bool higherIsDefect = rMean >= oMean;

            double proposedThreshold;
            string separationKind;
            double projectedCutPercent;
            string changeCode;
            string explanation;

            if (higherIsDefect)
            {
                compOp = ">";
                if (oMax < rMin)
                {
                    // Perfect separation: Gold Threshold!
                    proposedThreshold = Math.Round((oMax + rMin) / 2.0, 1);
                    separationKind = "CompletelySeparated";
                    projectedCutPercent = 100.0;
                    changeCode = $"{feat} > {proposedThreshold}";
                    explanation = $"100% Separated: Overkill max ({oMax}) < Real Defect min ({rMin}). Setting threshold > {proposedThreshold} eliminates 100% Overkill without defect leakage!";
                }
                else
                {
                    // Overlap: Zero-Leakage policy: threshold cannot exceed rMin!
                    proposedThreshold = Math.Round(rMin, 1);
                    var cutCount = oVals.Count(v => v < proposedThreshold);
                    projectedCutPercent = Math.Round((double)cutCount / oVals.Count * 100, 1);

                    if (projectedCutPercent >= 30.0)
                    {
                        separationKind = "SignificantShift";
                        changeCode = $"{feat} > {proposedThreshold}";
                        explanation = $"Significant shift: Safe threshold > {proposedThreshold} (near Real Defect min) cuts ~{projectedCutPercent}% Overkill with zero leakage.";
                    }
                    else
                    {
                        separationKind = "Overlapped";
                        changeCode = $"{feat} > {proposedThreshold}";
                        explanation = $"Overlapped: Feature distributions overlap ({projectedCutPercent}% reduction). Additional compound conditions recommended.";
                    }
                }
            }
            else
            {
                compOp = "<";
                if (oMin > rMax)
                {
                    // Perfect separation inverse: e.g. GrayMin
                    proposedThreshold = Math.Round((oMin + rMax) / 2.0, 1);
                    separationKind = "CompletelySeparated";
                    projectedCutPercent = 100.0;
                    changeCode = $"{feat} < {proposedThreshold}";
                    explanation = $"100% Separated: Overkill min ({oMin}) > Real Defect max ({rMax}). Setting threshold < {proposedThreshold} eliminates 100% Overkill!";
                }
                else
                {
                    proposedThreshold = Math.Round(rMax, 1);
                    var cutCount = oVals.Count(v => v > proposedThreshold);
                    projectedCutPercent = Math.Round((double)cutCount / oVals.Count * 100, 1);

                    if (projectedCutPercent >= 30.0)
                    {
                        separationKind = "SignificantShift";
                        changeCode = $"{feat} < {proposedThreshold}";
                        explanation = $"Significant shift: Setting threshold < {proposedThreshold} safely cuts ~{projectedCutPercent}% Overkill.";
                    }
                    else
                    {
                        separationKind = "Overlapped";
                        changeCode = $"{feat} < {proposedThreshold}";
                        explanation = $"Overlapped: Overkill and Real Defect feature distributions overlap.";
                    }
                }
            }

            results.Add(new FeatureSeparationResult(
                feat,
                currentThreshold,
                proposedThreshold,
                compOp,
                oMin,
                oMax,
                oMean,
                rMin,
                rMax,
                rMean,
                separationKind,
                projectedCutPercent,
                changeCode,
                explanation
            ));
        }

        return results
            .OrderByDescending(r => r.SeparationKind == "CompletelySeparated")
            .ThenByDescending(r => r.ProjectedOverkillCutPercent)
            .ToList();
    }

    /// <summary>
    /// Generates an optimized Python-like script by replacing sensitive static thresholds with Gold Threshold values.
    /// </summary>
    public static string GenerateTunedScript(string originalScript, IReadOnlyList<FeatureSeparationResult> separations)
    {
        if (string.IsNullOrWhiteSpace(originalScript)) return originalScript;

        var tuned = originalScript;
        var headerComments = new List<string> { "# === AUTO-TUNED SCRIPT BASED ON OVERKILL DATA ===" };

        foreach (var sep in separations.Where(s => s.ProjectedOverkillCutPercent >= 25.0))
        {
            // Replace pattern: Feature op Number
            var pattern = $@"\b({Regex.Escape(sep.FeatureName)}\s*(?:>|<|>=|<=|==)\s*)([\d.]+)";
            if (Regex.IsMatch(tuned, pattern, RegexOptions.IgnoreCase))
            {
                tuned = Regex.Replace(tuned, pattern, m => $"{sep.FeatureName} {sep.ComparisonOperator} {sep.ProposedThreshold}", RegexOptions.IgnoreCase);
                headerComments.Add($"# Optimized [{sep.FeatureName}]: Set to {sep.ComparisonOperator} {sep.ProposedThreshold} (Est. Overkill cut: {sep.ProjectedOverkillCutPercent}%)");
            }
        }

        return string.Join("\r\n", headerComments) + "\r\n\r\n" + tuned;
    }

    public sealed record WhatIfSimResult(
        int TotalEvaluated,
        int BaselineRealDefectCount,
        int BaselineOverkillCount,
        double BaselineOverkillRate,
        int BaselineLeakageCount,
        int CandidateRealDefectCount,
        int CandidateOverkillCount,
        double CandidateOverkillRate,
        int CandidateLeakageCount,
        int OverkillReducedCount,
        double OverkillReductionPercent,
        int NewLeakageIntroducedCount,
        bool IsSafeToDeploy,
        double EstimatedSavedOpHoursPerMonth,
        string DetailedExplanation,
        List<DivergenceSample> Divergences
    );

    public sealed record DivergenceSample(
        string KeyId,
        bool GroundTruthIsDefect,
        bool BaselineVerdict,
        bool CandidateVerdict,
        string DivergenceKind, // "OverkillEliminated" or "DangerousLeakage"
        Dictionary<string, double> Features,
        string Explanation
    );

    /// <summary>
    /// Executes side-by-side What-If simulation comparing BaselineScript vs CandidateScript on real historical samples.
    /// </summary>
    public static WhatIfSimResult SimulateWhatIf(
        string baselineScript,
        string candidateScript,
        IReadOnlyList<FeatureSample> samples)
    {
        var parsedBaseline = ScriptConditionEngine.Parse(baselineScript);
        var parsedCandidate = ScriptConditionEngine.Parse(candidateScript);

        var obs = samples.Select((s, idx) => (
            RowIndex: idx + 1,
            Features: s.Features,
            Observation: (ObservationReference?)new ObservationReference(
                Guid.Empty, idx + 1, s.KeyId, s.KeyId, s.LineCode, s.MachineCode, null, null, null, null, s.DefectName, null, s.InspectedAtUtc ?? DateTimeOffset.UtcNow
            )
        )).ToList();

        var evalBaseline = ScriptConditionEngine.EvaluateImperative(baselineScript, parsedBaseline, obs);
        var evalCandidate = ScriptConditionEngine.EvaluateImperative(candidateScript, parsedCandidate, obs);

        int baseReal = 0, baseOverkill = 0, baseLeakage = 0;
        int candReal = 0, candOverkill = 0, candLeakage = 0;
        var divergences = new List<DivergenceSample>();

        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            var bVerdict = evalBaseline.Evaluations[i].FinalResult;
            var cVerdict = evalCandidate.Evaluations[i].FinalResult;
            var isTrueDefect = s.IsGroundTruthRealDefect;

            // Baseline assessment
            if (bVerdict && isTrueDefect) baseReal++;
            else if (bVerdict && !isTrueDefect) baseOverkill++;
            else if (!bVerdict && isTrueDefect) baseLeakage++;

            // Candidate assessment
            if (cVerdict && isTrueDefect) candReal++;
            else if (cVerdict && !isTrueDefect) candOverkill++;
            else if (!cVerdict && isTrueDefect) candLeakage++;

            // Detect divergence
            if (bVerdict != cVerdict)
            {
                if (bVerdict && !cVerdict && !isTrueDefect)
                {
                    // Excellent: Overkill was eliminated!
                    divergences.Add(new DivergenceSample(
                        s.KeyId,
                        false,
                        true,
                        false,
                        "OverkillEliminated",
                        s.Features,
                        "✓ Success: The updated script eliminated this overkill false alarm."
                    ));
                }
                else if (bVerdict && !cVerdict && isTrueDefect)
                {
                    // Dangerous: Real defect missed by candidate script!
                    divergences.Add(new DivergenceSample(
                        s.KeyId,
                        true,
                        true,
                        false,
                        "DangerousLeakage",
                        s.Features,
                        "🚨 DANGER: Candidate script missed this real defect! Threshold must be relaxed."
                    ));
                }
            }
        }

        var overkillReduced = baseOverkill - candOverkill;
        var overkillCutPct = baseOverkill > 0 ? Math.Round((double)overkillReduced / baseOverkill * 100, 1) : 0.0;
        var newLeakage = candLeakage - baseLeakage;
        var isSafe = newLeakage <= 0 && candLeakage == 0;

        var baseTotalNg = baseReal + baseOverkill;
        var candTotalNg = candReal + candOverkill;

        var baseOverkillRate = baseTotalNg > 0 ? Math.Round((double)baseOverkill / baseTotalNg, 4) : 0.0;
        var candOverkillRate = candTotalNg > 0 ? Math.Round((double)candOverkill / candTotalNg, 4) : 0.0;

        // Estimated savings: assuming ~1.5 minutes for OP to re-inspect and handle each overkill alarm,
        // projected across 30 days based on sample batch
        var savedHours = Math.Round((overkillReduced * 1.5) / 60.0 * 20.0, 1);

        string explanation;
        if (isSafe && overkillReduced > 0)
        {
            explanation = $"✓ ZERO LEAKAGE VERIFIED: Candidate script eliminates {overkillReduced} overkill alarms ({overkillCutPct}%), reducing overkill rate from {baseOverkillRate:P1} down to {candOverkillRate:P1}. Estimated ~{savedHours} OP inspection hours saved per month!";
        }
        else if (!isSafe)
        {
            explanation = $"🚨 CRITICAL RISK: Candidate script introduced {newLeakage} defect escape(s) (Leakage)! Do not deploy this script to production.";
        }
        else
        {
            explanation = "Both script versions produced identical classification results on the test dataset.";
        }

        return new WhatIfSimResult(
            samples.Count,
            baseReal,
            baseOverkill,
            baseOverkillRate,
            baseLeakage,
            candReal,
            candOverkill,
            candOverkillRate,
            candLeakage,
            overkillReduced,
            overkillCutPct,
            newLeakage,
            isSafe,
            savedHours,
            explanation,
            divergences.Take(50).ToList()
        );
    }
}
