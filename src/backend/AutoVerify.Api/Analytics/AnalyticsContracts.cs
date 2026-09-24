using System.Text.Json.Serialization;

namespace AutoVerify.Api.Analytics;

public sealed record ObservationReference(
    Guid ObservationId,
    int RowIndex,
    string? DefectId = null,
    string? KeyId = null,
    string? LineCode = null,
    string? MachineCode = null,
    string? StationCode = null,
    string? ClassifierRuleCode = null,
    string? ClassifierRuleVersion = null,
    string? DefectFamily = null,
    string? DefectClass = null,
    string? Judge = null,
    DateTimeOffset? InspectedAtUtc = null
);

public sealed record FeatureSummaryDto(
    string Name,
    int Count,
    int MissingCount,
    double MissingRatio,
    double Min,
    double Max,
    double Mean,
    double StdDev
);

public sealed record AnalysisReadinessDto(
    string AnalysisType,
    bool IsReady,
    double ReadinessScore, // 0.0 to 1.0
    List<string> RecommendedFeatures,
    List<string> Blockers,
    List<string> Warnings
);

public sealed class AnalyticsScopeFilter
{
    public string? ProductKey { get; set; }
    public string? LineCode { get; set; }
    public string? MachineCode { get; set; }
    public string? StationCode { get; set; }
    public string? ClassifierRuleCode { get; set; }
    public string? ClassifierRuleVersion { get; set; }
    public string? DefectFamily { get; set; }
    public string? DefectClass { get; set; }
    public string? DefectType { get; set; }
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public DateTimeOffset? TimeFromUtc { get; set; }
    public DateTimeOffset? TimeToUtc { get; set; }
}

public sealed record FacetItem(string Value, int Count);

public sealed record SessionScopeFacetsResponse(
    Guid SessionId,
    int TotalRecords,
    List<FacetItem> Lines,
    List<FacetItem> Machines,
    List<FacetItem> Stations,
    List<FacetItem> ClassifierRules,
    List<FacetItem> ClassifierRuleVersions,
    List<FacetItem> DefectFamilies,
    List<FacetItem> DefectTypes,
    List<FacetItem> Features
);

public sealed record FeatureSchemaReadinessResponse(
    Guid SessionId,
    int TotalRecords,
    bool IsNgOnlyDataset,
    List<FeatureSummaryDto> Features,
    Dictionary<string, AnalysisReadinessDto> Readiness,
    List<string>? DetectedRuleVersions = null,
    List<string>? ScopeWarnings = null
);

public sealed class CreateAnalyticsJobRequest
{
    public string AnalysisType { get; set; } = string.Empty; // SPC, PCA, MSA, EWMA, CUSUM, DOE, FEATURE_RELATIONSHIP, KMEANS, GMM, ISOLATION_FOREST
    public string? FeatureName { get; set; }
    public List<string>? FeatureNames { get; set; }
    public string? PartField { get; set; }
    public string? OperatorField { get; set; }
    public string? ResponseFeature { get; set; }
    public List<string>? FactorFeatures { get; set; }
    public string? SubgroupIdField { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
    public int TtlMinutes { get; set; } = 120;
    public int MaxDataPoints { get; set; } = 20000;

    // Optional Scope Filters
    public AnalyticsScopeFilter? ScopeFilter { get; set; }
    public string? ProductKey { get; set; }
    public string? LineCode { get; set; }
    public string? MachineCode { get; set; }
    public string? StationCode { get; set; }
    public string? ClassifierRuleCode { get; set; }
    public string? ClassifierRuleVersion { get; set; }
    public string? DefectFamily { get; set; }
    public string? DefectType { get; set; }
    public DateTimeOffset? TimeFromUtc { get; set; }
    public DateTimeOffset? TimeToUtc { get; set; }

    // Preprocessing & Seed Options
    public int? Seed { get; set; }
    public string? MissingValuePolicy { get; set; } // CompleteCase, PairwiseComplete, MedianImpute, RejectMissing
    public string? ScalingPolicy { get; set; } // None, Standard, Robust

    // Algorithm-specific options
    public int? K { get; set; } // for K-means (default 3)
    public int? ComponentsCount { get; set; } // for GMM (default 3)
    public string? CovarianceType { get; set; } // "diagonal" or "full"
    public int? TreesCount { get; set; } // for Isolation Forest (default 100)
    public int? SubsampleSize { get; set; } // for Isolation Forest (default 256)
    public double? Contamination { get; set; } // for Isolation Forest (default 0.1)
    public int? MaxScatterPoints { get; set; } // for Feature Relationship
    public bool? AllowRuleVersionMixing { get; set; } // for SPC

    // Script Condition Analysis
    public string? ScriptText { get; set; } // Python-like AOI classifier script
    public int? MaxEvaluationRows { get; set; } // max rows to include in per-row evaluation output (default 500)
}

public sealed record AnalyticsJobDto(
    Guid JobId,
    Guid SessionId,
    string AnalysisType,
    string Status,
    int ProgressPercent,
    string RequestHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? ErrorMessage,
    List<string>? Warnings,
    object? Result
)
{
    public Guid Id => JobId;
}

// -------------------------------------------------------------
// Engine Result Contracts
// -------------------------------------------------------------

public sealed record FeatureRelationshipPair(
    string FeatureA,
    string FeatureB,
    double? Pearson,
    double? Spearman,
    int OverlapCount,
    double? PValue,
    string Strength
);

public sealed record RelationshipScatterPoint(
    ObservationReference Observation,
    double X,
    double Y
);

public sealed record FeatureRelationshipResult(
    List<string> Features,
    double?[][] PearsonMatrix,
    double?[][] SpearmanMatrix,
    int[][] OverlapCounts,
    Dictionary<string, int> MissingCounts,
    Dictionary<string, double> MissingRatios,
    List<FeatureRelationshipPair> RankedPairs,
    List<RelationshipScatterPoint> ScatterPoints,
    int TotalObservations,
    int IncludedObservations
);

public sealed record KMeansClusterSummary(
    int ClusterIndex,
    int Size,
    double Percentage,
    double[] ScaledCentroid,
    double[] OriginalCentroid,
    double MeanDistance
);

public sealed record KMeansAssignment(
    ObservationReference Observation,
    int ClusterIndex,
    double DistanceToCentroid
);

public sealed record KMeansResult(
    int K,
    double Inertia,
    int Iterations,
    bool Converged,
    int StartsRun,
    double? SilhouetteScore,
    List<KMeansClusterSummary> ClusterSummaries,
    List<KMeansAssignment> Assignments,
    List<string> SelectedFeatures,
    double[] Centers,
    double[] Scales
);

public sealed record GmmComponentSummary(
    int ComponentIndex,
    double Weight,
    int Size,
    double[] MeanVector,
    double[] VarianceVector
);

public sealed record GmmAssignment(
    ObservationReference Observation,
    int AssignedComponent,
    double MaxProbability,
    double[] Probabilities,
    double LogDensity,
    double Entropy
);

public sealed record GaussianMixtureResult(
    int ComponentsCount,
    string CovarianceType,
    double LogLikelihood,
    double Aic,
    double Bic,
    bool Converged,
    int Iterations,
    double[] Weights,
    double[][] Means,
    double[][] Variances, // diagonal variances
    double[][][]? FullCovariances, // full covariances if requested & computed
    List<GmmComponentSummary> ComponentSummaries,
    List<GmmAssignment> Assignments,
    List<string> SelectedFeatures
);

public sealed record IsolationForestAssignment(
    ObservationReference Observation,
    double AnomalyScore,
    bool IsAnomaly,
    double NormalizedPathLength
);

public sealed record TopAnomalyItem(
    ObservationReference Observation,
    double AnomalyScore,
    int Rank,
    Dictionary<string, double> FeatureContributions
);

public sealed record IsolationForestResult(
    int TreesCount,
    int SubsampleSize,
    double Contamination,
    double ScoreThreshold,
    int TotalObservations,
    int AnomalyCount,
    List<string> SelectedFeatures,
    Dictionary<string, double> FeatureContributions,
    List<IsolationForestAssignment> Assignments,
    List<TopAnomalyItem> TopAnomalies
);
