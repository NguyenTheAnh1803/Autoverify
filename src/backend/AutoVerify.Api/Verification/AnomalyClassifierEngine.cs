using System.Globalization;
using AutoVerify.Api.Analytics;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Services;

namespace AutoVerify.Api.Verification;

/// <summary>
/// Hybrid Decision & Anomaly Classifier Engine.
/// Evaluates product and defect records by orchestrating:
/// 1. AOI Machine Classifier Rules & Python Scripts (ScriptConditionEngine).
/// 2. Master Deep Learning AI Verdicts (AiVerdictMatrix / AiVerdictEntity).
/// 3. Operator Review Ground Truth (OpReviewMark).
/// Integrates these inputs into a unified Confidence Cascade verdict (Consensus, Rule Fast-Path, AI Verified, Conflict).
/// </summary>
public static class AnomalyClassifierEngine
{
    public sealed record DefectClassificationResult(
        DefectRecordEntity Defect,
        bool? IsTrueDefect,
        string DecisionVerdict, // "RealNG", "Overkill", "NA", "SuspectConflict"
        string RuleCode,
        string RuleVersion,
        string DecisionReason,
        Dictionary<string, double> ExtractedFeatures,
        string? AiVerdict = null,
        string? AiStatus = null,
        double? AiAnomalyScore = null,
        string? DecisionRoute = null // "HYBRID_CONSENSUS", "RULE_FAST_PATH", "AI_DEEPLEARNING", "HYBRID_CONFLICT", "UNCLASSIFIED"
    );

    public sealed record ProductAnomalyEvaluation(
        ProductRecordEntity Product,
        IReadOnlyList<DefectClassificationResult> DefectResults,
        string AnomalyCategory, // "RealNG", "Overkill", "NA", "Leakage", "ConsistentOK", "SuspectConflict"
        double AnomalyScore,
        string VerdictReason,
        int TrueDefectsCount,
        int FalseAlarmCount,
        string? PrimaryDecisionRoute = null,
        string? OverallAiStatus = null
    );

    /// <summary>
    /// Classifies an individual defect using Hybrid Decision Cascade (Rule script + AI Deep Learning).
    /// </summary>
    public static DefectClassificationResult ClassifyDefect(
        DefectRecordEntity defect,
        ProductRecordEntity product,
        IReadOnlyCollection<MachineClassifierRuleEntity> rules,
        AiVerdictEntity? aiVerdict = null)
    {
        var features = ExtractAllFeatures(defect);
        var rule = ResolveBestRule(defect, product, rules);
        var hasScript = rule != null && !string.IsNullOrWhiteSpace(rule.ScriptContent);

        // Extract AI verdict data if present
        var aiV = aiVerdict != null && aiVerdict.Status == AiVerdictStatus.Done
            ? aiVerdict.Verdict?.Trim().ToUpperInvariant()
            : null;
        var aiScore = aiVerdict?.AnomalyScore;
        var aiStatus = aiVerdict?.Status;

        if (hasScript)
        {
            try
            {
                var parsedScript = ScriptConditionEngine.Parse(rule!.ScriptContent!);
                var obs = new List<(int RowIndex, Dictionary<string, double> Features, ObservationReference? Observation)>
                {
                    (1, features, new ObservationReference(
                        defect.Id, 1, defect.Id.ToString(), defect.KeyId, product.LineCode, product.MachineCode,
                        defect.EqpName, rule.RuleCode, rule.Version, rule.DefectFamily, rule.DefectClass, defect.Judge,
                        defect.CreatedAtUtc))
                };

                var evalResult = ScriptConditionEngine.EvaluateImperative(rule.ScriptContent!, parsedScript, obs);
                var ev = evalResult.Evaluations.FirstOrDefault();
                var scriptVerdict = ev?.FinalResult ?? false;

                var guardNote = ev?.GuardFailed == true ? $" [Guard failed: {ev.FailedGuardFeature}]" : "";
                var condSummary = ev != null
                    ? string.Join(", ", ev.ConditionResults.Select(kv => $"{kv.Key}={(kv.Value ? "T" : "F")}"))
                    : "No eval";

                if (scriptVerdict)
                {
                    if (aiV == "NG")
                    {
                        var reason = $"Confirmed Real NG by Consensus: Rule [{rule.RuleName}] evaluated TRUE ({condSummary}) and AI verified NG (score: {aiScore:F2}).";
                        return new DefectClassificationResult(defect, true, "RealNG", rule.RuleCode, rule.Version, reason, features, aiV, aiStatus, aiScore, "HYBRID_CONSENSUS");
                    }
                    if (aiV == "OK")
                    {
                        var reason = $"Conflict: Rule [{rule.RuleName}] evaluated TRUE ({condSummary}), but AI verified image as OK (score: {aiScore:F2}). Manual inspection advised.";
                        return new DefectClassificationResult(defect, true, "SuspectConflict", rule.RuleCode, rule.Version, reason, features, aiV, aiStatus, aiScore, "HYBRID_CONFLICT");
                    }

                    var ruleReason = $"Confirmed Real NG: Script [{rule.RuleName}] (Priority {rule.Priority}) evaluated TRUE ({condSummary}).";
                    return new DefectClassificationResult(defect, true, "RealNG", rule.RuleCode, rule.Version, ruleReason, features, aiV, aiStatus, aiScore, "RULE_FAST_PATH");
                }
                else
                {
                    if (aiV == "OK")
                    {
                        var reason = $"Confirmed Overkill by Consensus: Rule [{rule.RuleName}] cleared defect ({condSummary}{guardNote}) and AI verified OK.";
                        return new DefectClassificationResult(defect, false, "Overkill", rule.RuleCode, rule.Version, reason, features, aiV, aiStatus, aiScore, "HYBRID_CONSENSUS");
                    }
                    if (aiV == "NG")
                    {
                        var reason = $"Conflict: Rule [{rule.RuleName}] cleared defect ({condSummary}{guardNote}), but AI flagged NG (score: {aiScore:F2}).";
                        return new DefectClassificationResult(defect, false, "SuspectConflict", rule.RuleCode, rule.Version, reason, features, aiV, aiStatus, aiScore, "HYBRID_CONFLICT");
                    }

                    var reasonScriptFalse = $"Script [{rule.RuleName}] evaluated FALSE ({condSummary}{guardNote}). No dedicated overkill algorithm, marked as NA.";
                    return new DefectClassificationResult(defect, false, "NA", rule.RuleCode, rule.Version, reasonScriptFalse, features, aiV, aiStatus, aiScore, "RULE_FAST_PATH");
                }
            }
            catch (Exception ex)
            {
                var reason = $"Script execution error in [{rule!.RuleCode}]: {ex.Message}. Marked as NA.";
                return new DefectClassificationResult(defect, null, "NA", rule.RuleCode, rule.Version, reason, features, aiV, aiStatus, aiScore, "UNCLASSIFIED");
            }
        }

        // When NO classifier script is configured:
        var fallbackRuleCode = rule?.RuleCode ?? "NO_SCRIPT";
        var fallbackRuleVer = rule?.Version ?? "1.0";

        if (aiV == "OK")
        {
            var reason = $"Confirmed Overkill by AI Deep Learning: AI model verified defect '{defect.DefectName}' as OK (score: {aiScore:F2}).";
            return new DefectClassificationResult(defect, false, "Overkill", fallbackRuleCode, fallbackRuleVer, reason, features, aiV, aiStatus, aiScore, "AI_DEEPLEARNING");
        }
        if (aiV == "NG")
        {
            var reason = $"Confirmed Real NG by AI Deep Learning: AI model verified defect '{defect.DefectName}' as NG (score: {aiScore:F2}).";
            return new DefectClassificationResult(defect, true, "RealNG", fallbackRuleCode, fallbackRuleVer, reason, features, aiV, aiStatus, aiScore, "AI_DEEPLEARNING");
        }

        var unclassifiedReason = $"N/A: No classifier script configured for defect '{defect.DefectName}' and AI verdict pending/unavailable.";
        return new DefectClassificationResult(defect, null, "NA", fallbackRuleCode, fallbackRuleVer, unclassifiedReason, features, aiV, aiStatus, aiScore, "UNCLASSIFIED");
    }

    /// <summary>
    /// Evaluates a product and all its linked defects using Hybrid Decision Fusion (Rules + AI + OP).
    /// </summary>
    public static ProductAnomalyEvaluation EvaluateProduct(
        ProductRecordEntity product,
        IReadOnlyCollection<DefectRecordEntity> defects,
        IReadOnlyCollection<MachineClassifierRuleEntity> rules,
        IReadOnlyCollection<AiVerdictEntity>? aiVerdicts = null)
    {
        var aiByDefectId = aiVerdicts?
            .Where(v => v.DefectId != Guid.Empty)
            .GroupBy(v => v.DefectId)
            .ToDictionary(g => g.Key, g => g.First()) ?? [];

        var defectResults = defects.Select(d =>
        {
            aiByDefectId.TryGetValue(d.Id, out var aiV);
            return ClassifyDefect(d, product, rules, aiV);
        }).ToList();

        var isStationNg = product.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("N", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("NA", StringComparison.OrdinalIgnoreCase);

        var isStationPass = product.Judge.Equals("G", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("OK", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("GOOD", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("PASS", StringComparison.OrdinalIgnoreCase);

        AiFinalStatus? overallAiStatus = null;
        if (aiVerdicts != null && aiVerdicts.Count > 0)
        {
            var defectAiItems = aiVerdicts.Select(v => new AiDefectVerdict(v.Status, v.Verdict)).ToList();
            overallAiStatus = AiVerdictMatrix.Decide(isStationNg, defectAiItems);
        }

        // LEVEL 0: OP Review Override (Ground Truth)
        if (product.OpReviewMark == "Real Defect")
        {
            return new ProductAnomalyEvaluation(
                product, defectResults, "RealNG", 0.05,
                "Confirmed Real NG: Verified by Operator Review (Ground Truth).",
                defectResults.Count, 0, "OP_OVERRIDE", overallAiStatus?.ToString());
        }
        if (product.OpReviewMark == "Overkill")
        {
            return new ProductAnomalyEvaluation(
                product, defectResults, "Overkill", 0.95,
                "Confirmed Overkill: Verified by Operator Review (Ground Truth).",
                0, defectResults.Count, "OP_OVERRIDE", overallAiStatus?.ToString());
        }

        var realNgDefects = defectResults.Where(r => r.DecisionVerdict == "RealNG").ToList();
        var overkillDefects = defectResults.Where(r => r.DecisionVerdict == "Overkill").ToList();
        var conflictDefects = defectResults.Where(r => r.DecisionVerdict == "SuspectConflict").ToList();

        if (isStationNg)
        {
            // Case 1: Station NG and at least 1 real NG confirmed (by Rule, AI, or Consensus)
            if (realNgDefects.Count > 0)
            {
                var topDefect = realNgDefects[0];
                var route = realNgDefects.Any(d => d.DecisionRoute == "HYBRID_CONSENSUS")
                    ? "HYBRID_CONSENSUS"
                    : (realNgDefects.Any(d => d.DecisionRoute == "AI_DEEPLEARNING") ? "AI_DEEPLEARNING" : "RULE_FAST_PATH");

                return new ProductAnomalyEvaluation(
                    product,
                    defectResults,
                    "RealNG",
                    0.05,
                    $"Confirmed Real NG: {realNgDefects.Count} defect(s) verified ({topDefect.DecisionReason}).",
                    realNgDefects.Count,
                    defectResults.Count - realNgDefects.Count,
                    route,
                    overallAiStatus?.ToString()
                );
            }

            // Case 2: Station NG, but defects confirmed as Overkill (or overall AI status is OverkillConfirmed)
            if ((defectResults.Count > 0 && overkillDefects.Count == defectResults.Count) ||
                overallAiStatus == AiFinalStatus.OverkillConfirmed)
            {
                var route = overkillDefects.Any(d => d.DecisionRoute == "HYBRID_CONSENSUS")
                    ? "HYBRID_CONSENSUS"
                    : (overkillDefects.Any(d => d.DecisionRoute == "AI_DEEPLEARNING") || overallAiStatus == AiFinalStatus.OverkillConfirmed
                        ? "AI_DEEPLEARNING"
                        : "RULE_FAST_PATH");

                return new ProductAnomalyEvaluation(
                    product,
                    defectResults,
                    "Overkill",
                    0.92,
                    $"Confirmed Overkill: Station judged '{product.Judge}' (NG) but all defect(s) verified as False Alarms (Overkill).",
                    0,
                    defectResults.Count,
                    route,
                    overallAiStatus?.ToString()
                );
            }

            // Case 3: Conflicting verdicts (Rule NG vs AI OK or vice versa)
            if (conflictDefects.Count > 0)
            {
                return new ProductAnomalyEvaluation(
                    product,
                    defectResults,
                    "SuspectConflict",
                    0.50,
                    $"Suspect Anomaly: Conflict between Rule script and AI Deep Learning on {conflictDefects.Count} defect(s). Inspection required.",
                    0,
                    defectResults.Count,
                    "HYBRID_CONFLICT",
                    overallAiStatus?.ToString()
                );
            }

            // Case 4: Station NG with 0 defects
            if (defectResults.Count == 0)
            {
                return new ProductAnomalyEvaluation(
                    product,
                    defectResults,
                    "Overkill",
                    0.88,
                    $"Station judged '{product.Judge}' (NG) with 0 defect records found across all defect tables (False Alarm / Overkill).",
                    0,
                    0,
                    "RULE_FAST_PATH",
                    overallAiStatus?.ToString()
                );
            }

            // Case 5: Unclassified (N/A)
            return new ProductAnomalyEvaluation(
                product,
                defectResults,
                "NA",
                0.50,
                $"N/A: Station reported NG, but no classifier script configured and AI verdict not conclusive ({defectResults.Count} defect(s)). Marked as NA.",
                0,
                defectResults.Count,
                "UNCLASSIFIED",
                overallAiStatus?.ToString()
            );
        }

        if (isStationPass)
        {
            if (realNgDefects.Count > 0 || overallAiStatus == AiFinalStatus.LeakageConfirmed)
            {
                var topEscape = realNgDefects.FirstOrDefault();
                var reason = topEscape != null
                    ? $"Defect Escape (Leakage): Station passed product ('{product.Judge}') but verified real defect: {topEscape.Defect.DefectName}."
                    : "Defect Escape (Leakage): Station passed product, but AI Deep Learning detected an NG image.";

                return new ProductAnomalyEvaluation(
                    product,
                    defectResults,
                    "Leakage",
                    0.98,
                    reason,
                    realNgDefects.Count,
                    defectResults.Count - realNgDefects.Count,
                    "LEAKAGE_DETECTED",
                    overallAiStatus?.ToString()
                );
            }

            if (defectResults.Count == 0 || overkillDefects.Count == defectResults.Count || overallAiStatus == AiFinalStatus.ConsistentOk)
            {
                return new ProductAnomalyEvaluation(
                    product,
                    defectResults,
                    "ConsistentOK",
                    0.01,
                    "Clean product: Station passed with zero true defect records.",
                    0,
                    0,
                    "CLEAN_PASS",
                    overallAiStatus?.ToString()
                );
            }

            return new ProductAnomalyEvaluation(
                product,
                defectResults,
                "NA",
                0.20,
                $"N/A: Station passed but contains {defectResults.Count} unclassified defect record(s). Marked as NA.",
                0,
                defectResults.Count,
                "UNCLASSIFIED",
                overallAiStatus?.ToString()
            );
        }

        return new ProductAnomalyEvaluation(
            product,
            defectResults,
            "NA",
            0.0,
            $"Station judgment '{product.Judge}' not classified.",
            realNgDefects.Count,
            defectResults.Count - realNgDefects.Count,
            "UNCLASSIFIED",
            overallAiStatus?.ToString()
        );
    }

    /// <summary>
    /// Matches the most specific active classifier rule with a script for a given defect.
    /// Respects Priority order (Priority 0 first, then 1, etc.).
    /// </summary>
    public static MachineClassifierRuleEntity? ResolveBestRule(
        DefectRecordEntity defect,
        ProductRecordEntity product,
        IReadOnlyCollection<MachineClassifierRuleEntity> rules)
    {
        if (rules.Count == 0) return null;

        var scriptRules = rules.Where(r => r.IsActive && !string.IsNullOrWhiteSpace(r.ScriptContent)).ToList();
        if (scriptRules.Count == 0) return null;

        var candidates = scriptRules.Select(r =>
        {
            var score = 0;

            // 1. DefectClass or RuleName or RuleCode exact match with DefectName (Highest importance)
            if (!string.IsNullOrWhiteSpace(defect.DefectName))
            {
                var dName = defect.DefectName.Trim();
                if (string.Equals(r.RuleCode, dName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.RuleName, dName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.DefectClass, dName, StringComparison.OrdinalIgnoreCase))
                {
                    score += 64;
                }
                else if (r.DefectClass != null && (dName.Contains(r.DefectClass, StringComparison.OrdinalIgnoreCase) || r.DefectClass.Contains(dName, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 32;
                }
            }

            // 2. Station match
            if (!string.IsNullOrWhiteSpace(r.StationCode))
            {
                if (!string.IsNullOrWhiteSpace(defect.EqpName) && string.Equals(r.StationCode, defect.EqpName.Trim(), StringComparison.OrdinalIgnoreCase))
                    score += 16;
                else
                    return (Rule: r, Score: -1);
            }

            // 3. Machine match
            if (!string.IsNullOrWhiteSpace(r.MachineCode))
            {
                if (!string.IsNullOrWhiteSpace(product.MachineCode) && string.Equals(r.MachineCode, product.MachineCode.Trim(), StringComparison.OrdinalIgnoreCase))
                    score += 8;
                else
                    return (Rule: r, Score: -1);
            }

            // 4. Line match
            if (!string.IsNullOrWhiteSpace(r.LineCode))
            {
                if (!string.IsNullOrWhiteSpace(product.LineCode) && string.Equals(r.LineCode, product.LineCode.Trim(), StringComparison.OrdinalIgnoreCase))
                    score += 4;
                else
                    return (Rule: r, Score: -1);
            }

            // 5. Defect Family match
            if (!string.IsNullOrWhiteSpace(r.DefectFamily))
            {
                var fam = r.DefectFamily.Trim();
                if ((!string.IsNullOrWhiteSpace(defect.DefectType) && defect.DefectType.Contains(fam, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(defect.DefectTable) && defect.DefectTable.Contains(fam, StringComparison.OrdinalIgnoreCase)))
                    score += 2;
            }

            return (Rule: r, Score: score);
        })
        .Where(x => x.Score >= 0)
        .OrderByDescending(x => x.Score)
        .ThenBy(x => x.Rule.Priority)
        .ThenByDescending(x => x.Rule.EffectiveFromUtc)
        .ToList();

        return candidates.FirstOrDefault().Rule;
    }

    /// <summary>
    /// Combines standard defect coordinates, confidence, and features from JSON into a unified lookup.
    /// </summary>
    public static Dictionary<string, double> ExtractAllFeatures(DefectRecordEntity defect)
    {
        var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        dict["ConfidenceScore"] = defect.ConfidenceScore;
        if (defect.CoordinateX.HasValue) dict["CoordinateX"] = defect.CoordinateX.Value;
        if (defect.CoordinateY.HasValue) dict["CoordinateY"] = defect.CoordinateY.Value;
        if (defect.Priority.HasValue) dict["Priority"] = defect.Priority.Value;

        // Parse features from FeatureJson
        if (!string.IsNullOrWhiteSpace(defect.FeatureJson))
        {
            var parsed = DefectStreamParser.ExtractNumericFeaturesFromJson(defect.FeatureJson);
            foreach (var (name, value) in parsed)
            {
                dict[name] = value;
            }
        }

        // Parse features from CustomFeatureJson
        if (!string.IsNullOrWhiteSpace(defect.CustomFeatureJson))
        {
            var parsedCustom = DefectStreamParser.ExtractNumericFeaturesFromJson(defect.CustomFeatureJson);
            foreach (var (name, value) in parsedCustom)
            {
                dict[name] = value;
            }
        }

        return dict;
    }
}
