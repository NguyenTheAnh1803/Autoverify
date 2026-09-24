using System.Globalization;
using AutoVerify.Api.Analytics;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Services;

namespace AutoVerify.Api.Verification;

/// <summary>
/// Evaluates product and defect records using AOI Machine Classifier Rules & Scripts.
/// Note: Overkill is set to NA until a formal Overkill algorithm/script is configured.
/// Defect classification strictly requires a matching Script. Without a script, it is marked as NA.
/// </summary>
public static class AnomalyClassifierEngine
{
    public sealed record DefectClassificationResult(
        DefectRecordEntity Defect,
        bool? IsTrueDefect,
        string DecisionVerdict, // "RealNG", "NA"
        string RuleCode,
        string RuleVersion,
        string DecisionReason,
        Dictionary<string, double> ExtractedFeatures
    );

    public sealed record ProductAnomalyEvaluation(
        ProductRecordEntity Product,
        IReadOnlyList<DefectClassificationResult> DefectResults,
        string AnomalyCategory, // "RealNG", "NA", "Leakage", "ConsistentOK"
        double AnomalyScore,
        string VerdictReason,
        int TrueDefectsCount,
        int FalseAlarmCount
    );

    /// <summary>
    /// Classifies an individual defect strictly using a matching script.
    /// If no script is configured or matched, verdict is "NA".
    /// </summary>
    public static DefectClassificationResult ClassifyDefect(
        DefectRecordEntity defect,
        ProductRecordEntity product,
        IReadOnlyCollection<MachineClassifierRuleEntity> rules)
    {
        var features = ExtractAllFeatures(defect);
        var rule = ResolveBestRule(defect, product, rules);

        if (rule == null || string.IsNullOrWhiteSpace(rule.ScriptContent))
        {
            var ruleCode = rule?.RuleCode ?? "NO_SCRIPT";
            var ruleVer = rule?.Version ?? "1.0";
            var reason = $"N/A: No classifier script configured for defect '{defect.DefectName}'. Script is required for classification.";
            return new DefectClassificationResult(defect, null, "NA", ruleCode, ruleVer, reason, features);
        }

        try
        {
            var parsedScript = ScriptConditionEngine.Parse(rule.ScriptContent);
            var obs = new List<(int RowIndex, Dictionary<string, double> Features, ObservationReference? Observation)>
            {
                (1, features, new ObservationReference(
                    defect.Id, 1, defect.Id.ToString(), defect.KeyId, product.LineCode, product.MachineCode,
                    defect.EqpName, rule.RuleCode, rule.Version, rule.DefectFamily, rule.DefectClass, defect.Judge,
                    defect.CreatedAtUtc))
            };

            var evalResult = ScriptConditionEngine.EvaluateImperative(rule.ScriptContent, parsedScript, obs);
            var ev = evalResult.Evaluations.FirstOrDefault();
            var scriptVerdict = ev?.FinalResult ?? false;

            var guardNote = ev?.GuardFailed == true ? $" [Guard failed: {ev.FailedGuardFeature}]" : "";
            var condSummary = ev != null
                ? string.Join(", ", ev.ConditionResults.Select(kv => $"{kv.Key}={(kv.Value ? "T" : "F")}"))
                : "No eval";

            if (scriptVerdict)
            {
                var reason = $"Confirmed Real NG: Script [{rule.RuleName}] (Priority {rule.Priority}) evaluated TRUE ({condSummary}).";
                return new DefectClassificationResult(defect, true, "RealNG", rule.RuleCode, rule.Version, reason, features);
            }
            else
            {
                var reason = $"Script [{rule.RuleName}] evaluated FALSE ({condSummary}{guardNote}). No overkill algorithm yet, marked as NA.";
                return new DefectClassificationResult(defect, false, "NA", rule.RuleCode, rule.Version, reason, features);
            }
        }
        catch (Exception ex)
        {
            var reason = $"Script execution error in [{rule.RuleCode}]: {ex.Message}. Marked as NA.";
            return new DefectClassificationResult(defect, null, "NA", rule.RuleCode, rule.Version, reason, features);
        }
    }

    /// <summary>
    /// Evaluates a product and all its linked defects.
    /// Confirmed Real NG when script passes. Overkill is set to NA until a dedicated overkill algorithm/script exists.
    /// </summary>
    public static ProductAnomalyEvaluation EvaluateProduct(
        ProductRecordEntity product,
        IReadOnlyCollection<DefectRecordEntity> defects,
        IReadOnlyCollection<MachineClassifierRuleEntity> rules)
    {
        var defectResults = defects.Select(d => ClassifyDefect(d, product, rules)).ToList();

        var isStationNg = product.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("N", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("NA", StringComparison.OrdinalIgnoreCase);

        var isStationPass = product.Judge.Equals("G", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("OK", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("GOOD", StringComparison.OrdinalIgnoreCase)
            || product.Judge.Equals("PASS", StringComparison.OrdinalIgnoreCase);

        var realNgDefects = defectResults.Where(r => r.DecisionVerdict == "RealNG").ToList();

        if (isStationNg)
        {
            if (realNgDefects.Count > 0)
            {
                var topDefect = realNgDefects[0];
                return new ProductAnomalyEvaluation(
                    product,
                    defectResults,
                    "RealNG",
                    0.05,
                    $"Confirmed Real NG by script: {realNgDefects.Count} defect(s) satisfied script rule [{topDefect.RuleCode}].",
                    realNgDefects.Count,
                    defectResults.Count - realNgDefects.Count
                );
            }

            // Station reported NG, but no defect matched any script.
            var reason = defectResults.Count == 0
                ? "N/A: Station reported NG but no defect records found. No overkill algorithm exists yet, marked as NA."
                : $"N/A: Station reported NG, but no overkill classification script configured ({defectResults.Count} defect(s)). Marked as NA.";

            return new ProductAnomalyEvaluation(
                product,
                defectResults,
                "NA",
                0.50,
                reason,
                0,
                defectResults.Count
            );
        }

        if (isStationPass)
        {
            if (realNgDefects.Count > 0)
            {
                var topEscape = realNgDefects[0];
                return new ProductAnomalyEvaluation(
                    product,
                    defectResults,
                    "Leakage",
                    0.98,
                    $"Defect Escape (Leakage): Station passed product ('{product.Judge}') but script [{topEscape.RuleCode}] confirmed real defect: {topEscape.Defect.DefectName}.",
                    realNgDefects.Count,
                    defectResults.Count - realNgDefects.Count
                );
            }

            if (defects.Count == 0)
            {
                return new ProductAnomalyEvaluation(
                    product,
                    defectResults,
                    "ConsistentOK",
                    0.01,
                    "Clean product: Station passed with zero defect records.",
                    0,
                    0
                );
            }

            return new ProductAnomalyEvaluation(
                product,
                defectResults,
                "NA",
                0.20,
                $"N/A: Station passed but contains {defects.Count} defect record(s) without classification scripts. Marked as NA.",
                0,
                defects.Count
            );
        }

        return new ProductAnomalyEvaluation(
            product,
            defectResults,
            "NA",
            0.0,
            $"Station judgment '{product.Judge}' not classified.",
            realNgDefects.Count,
            defectResults.Count - realNgDefects.Count
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
