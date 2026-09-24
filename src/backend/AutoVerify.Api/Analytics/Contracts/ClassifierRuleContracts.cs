namespace AutoVerify.Api.Analytics.Contracts;

public sealed record ClassifierRuleDto(
    Guid Id,
    string RuleCode,
    string RuleName,
    string Version,
    string? ProductKey,
    string? LineCode,
    string? MachineCode,
    string? StationCode,
    string? DefectFamily,
    string? DefectClass,
    string ThresholdsConfigJson,
    string? ModelMetadataJson,
    bool IsActive,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string CreatedBy,
    string? Description,
    string? ScriptContent = null,
    int Priority = 0,
    string? DefectTypeId = null
);

public sealed record CreateClassifierRuleRequest(
    string RuleCode,
    string RuleName,
    string Version,
    string? ProductKey = null,
    string? LineCode = null,
    string? MachineCode = null,
    string? StationCode = null,
    string? DefectFamily = null,
    string? DefectClass = null,
    string? ThresholdsConfigJson = null,
    string? ModelMetadataJson = null,
    string? ScriptContent = null,
    bool IsActive = true,
    DateTimeOffset? EffectiveFromUtc = null,
    DateTimeOffset? EffectiveToUtc = null,
    string? Description = null,
    int Priority = 0,
    string? DefectTypeId = null
);

public sealed record UpdateClassifierRuleRequest(
    string? RuleName = null,
    string? ThresholdsConfigJson = null,
    string? ModelMetadataJson = null,
    string? ScriptContent = null,
    bool? IsActive = null,
    DateTimeOffset? EffectiveToUtc = null,
    string? Description = null,
    int? Priority = null,
    string? DefectTypeId = null
);

public sealed record AoiScriptRuleItem(
    int Priority,
    string Name,
    string? GUID,
    string? DefectTypeID,
    string? Script
);

public sealed record ImportAoiRulesJsonRequest(
    List<AoiScriptRuleItem>? Rules,
    string? MachineCode = null,
    string? LineCode = null
);

public sealed record ResolveClassifierRuleRequest(
    string? ProductKey = null,
    string? LineCode = null,
    string? MachineCode = null,
    string? StationCode = null,
    string? DefectFamily = null,
    string? DefectClass = null,
    string? RuleCode = null,
    DateTimeOffset? EffectiveTimeUtc = null
);

public sealed record ResolveClassifierRuleResponse(
    bool IsResolved,
    ClassifierRuleDto? Rule,
    string? MatchResolutionKind,
    int SpecificityScore,
    string? Message
);

public sealed record TestClassifierRuleRequest(
    string? ScriptContent = null,
    Guid? RuleId = null,
    Dictionary<string, double>? Features = null,
    string? FeaturesJson = null
);

public sealed record TestClassifierRuleResponse(
    bool Success,
    bool FinalResult,
    string Verdict,
    Dictionary<string, bool> GuardResults,
    Dictionary<string, bool> ConditionResults,
    Dictionary<string, double?> FeatureValues,
    bool GuardFailed,
    string? FailedGuardFeature,
    List<string> RequiredFeatures,
    List<string> Warnings,
    string? ErrorMessage = null
);

// -------------------------------------------------------------
// Rule Auto-Tuning & What-If Simulation Contracts
// -------------------------------------------------------------

public sealed record RuleTuningRecommendationDto(
    string FeatureName,
    double? CurrentThresholdInScript,
    double ProposedThreshold,
    string ComparisonOperator,
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

public sealed record RuleTuningAnalysisResponse(
    Guid? RuleId,
    string RuleCode,
    string RuleName,
    string DefectClass,
    int TotalOverkillSamples,
    int TotalRealDefectSamples,
    double CurrentOverkillRate,
    IReadOnlyCollection<RuleTuningRecommendationDto> Recommendations,
    string OriginalScript,
    string AutoTunedScript,
    string ExecutiveSummary
);

public sealed record SimulateWhatIfRequest(
    Guid? RuleId = null,
    string? BaselineScript = null,
    string? CandidateScript = null,
    string? DefectName = null,
    string? LineCode = null,
    string? MachineCode = null,
    int MaxSamples = 2000
);

public sealed record WhatIfDivergenceDto(
    string KeyId,
    bool GroundTruthIsDefect,
    bool BaselineVerdict,
    bool CandidateVerdict,
    string DivergenceKind, // "OverkillEliminated" or "DangerousLeakage"
    Dictionary<string, double> Features,
    string Explanation
);

public sealed record SimulateWhatIfResponse(
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
    IReadOnlyCollection<WhatIfDivergenceDto> Divergences
);
