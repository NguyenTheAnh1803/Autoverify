using System.Collections.Concurrent;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AutoVerify.Api.Analytics;

public sealed record SpcDriftAlertDto(
    Guid AlertId,
    string Severity, // "Critical", "Warning", "Info"
    string MachineCode,
    string LineCode,
    string FeatureName,
    int RuleId,
    string RuleName,
    double CenterLine,
    double CurrentValue,
    double Sigma,
    string DriftDirection, // "Increasing", "Decreasing", "Outlier"
    string RootCauseHypothesis,
    string RecommendedAction,
    DateTimeOffset DetectedAtUtc,
    string? SampleKeyId = null
);

public sealed record MachineCpkBenchmarkDto(
    string MachineCode,
    string LineCode,
    string FeatureName,
    int SampleSize,
    double Mean,
    double Sigma,
    double? Cp,
    double? Cpk,
    string CapabilityStatus, // "WorldClass", "Adequate", "Risky", "Incapable"
    bool HasActiveDrift,
    int TotalViolations
);

public sealed record SpcEarlyWarningDashboardResponse(
    int TotalEvaluatedRecords,
    int ActiveAlertsCount,
    int CriticalCount,
    int WarningCount,
    int InfoCount,
    IReadOnlyList<SpcDriftAlertDto> Alerts,
    IReadOnlyList<MachineCpkBenchmarkDto> Benchmarks,
    DateTimeOffset EvaluatedAtUtc
);

public sealed class SpcEarlyWarningService
{
    private readonly AutoVerifyDbContext _db;
    private static readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAtUtc, SpcEarlyWarningDashboardResponse Data)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public SpcEarlyWarningService(AutoVerifyDbContext db)
    {
        _db = db;
    }

    public async Task<SpcEarlyWarningDashboardResponse> GetEarlyWarningsAsync(
        string? machineCode = null,
        string? lineCode = null,
        int lookbackCount = 400,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{machineCode ?? "ALL"}_{lineCode ?? "ALL"}_{lookbackCount}";
        if (Cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cached.Data;
        }

        var query = _db.DefectRecords.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(machineCode) && !machineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(d => d.MachineCode == machineCode);
        if (!string.IsNullOrWhiteSpace(lineCode) && !lineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(d => d.LineCode == lineCode);

        var recentDefects = await query
            .Where(d => d.FeatureJson != null && d.FeatureJson != "" && d.FeatureJson != "{}")
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(Math.Clamp(lookbackCount, 50, 2000))
            .Select(d => new
            {
                d.Id,
                d.KeyId,
                MachineCode = string.IsNullOrWhiteSpace(d.MachineCode) ? "MCL" : d.MachineCode,
                LineCode = string.IsNullOrWhiteSpace(d.LineCode) ? "LINE-1" : d.LineCode,
                d.DefectName,
                d.FeatureJson,
                d.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        if (recentDefects.Count == 0)
        {
            var empty = new SpcEarlyWarningDashboardResponse(0, 0, 0, 0, 0, [], [], DateTimeOffset.UtcNow);
            Cache[cacheKey] = (DateTimeOffset.UtcNow.AddSeconds(15), empty);
            return empty;
        }

        // Extract features and organize into series per (Machine, Line, Feature)
        var featureSeries = new Dictionary<(string Machine, string Line, string Feature), List<(DateTimeOffset Time, double Value, string KeyId)>>();

        foreach (var def in recentDefects)
        {
            var numericMap = DefectStreamParser.ExtractNumericFeaturesFromJson(def.FeatureJson);
            foreach (var (featName, val) in numericMap)
            {
                // Skip identifiers / flags
                if (featName.Contains("ID", StringComparison.OrdinalIgnoreCase) ||
                    featName.Contains("IP", StringComparison.OrdinalIgnoreCase))
                    continue;

                var key = (def.MachineCode, def.LineCode, featName);
                if (!featureSeries.TryGetValue(key, out var list))
                {
                    list = [];
                    featureSeries[key] = list;
                }
                list.Add((def.CreatedAtUtc, val, def.KeyId));
            }
        }

        var alerts = new List<SpcDriftAlertDto>();
        var benchmarks = new List<MachineCpkBenchmarkDto>();

        foreach (var (key, rawPoints) in featureSeries)
        {
            // Need at least 10 points to run meaningful statistical tests
            if (rawPoints.Count < 10) continue;

            var ordered = rawPoints.OrderBy(p => p.Time).ToList();
            var valuesOnly = ordered.Select(p => p.Value).ToArray();

            // Calculate mean and sigma using Welford
            var welford = new WelfordAccumulator();
            foreach (var v in valuesOnly) welford.Add(v);

            var mean = welford.Mean;
            var sigma = welford.StandardDeviation;

            if (sigma <= 0.000001 || double.IsNaN(sigma) || double.IsInfinity(sigma))
                continue;

            // Evaluate Nelson Rules
            var violations = NelsonRulesEngine.Evaluate(valuesOnly, mean, sigma);

            // Check for recent violations (in the last 10 points)
            var n = valuesOnly.Length;
            var recentViolations = violations.Where(v => v.Index >= n - 10).ToList();
            var hasActiveDrift = recentViolations.Count > 0;

            // Generate Alerts for recent violations
            foreach (var v in recentViolations.GroupBy(x => x.RuleId).Select(g => g.Last()))
            {
                var pointIdx = v.Index;
                var currentVal = valuesOnly[pointIdx];
                var sampleKey = ordered[pointIdx].KeyId;
                var diff = currentVal - mean;

                string severity;
                string direction;
                string rootCause;
                string recommendation;

                switch (v.RuleId)
                {
                    case 1:
                        severity = "Critical";
                        direction = "Outlier";
                        rootCause = $"Điểm đo ({currentVal:F1}) đột biến vượt quá giới hạn 3-Sigma ({mean + 3 * sigma:F1}). Thường do va quẹt đồ gá, mẫu dị vật bất thường hoặc camera bị chói sáng tức thời.";
                        recommendation = "Dừng lô sản phẩm, soi lại ảnh khuyết tật và kiểm tra camera quang học trạm ngay lập tức.";
                        break;
                    case 2:
                        severity = "Warning";
                        direction = diff > 0 ? "Increasing" : "Decreasing";
                        rootCause = $"Quá trình bị trôi dạt (Drift): 9 điểm đo liên tiếp lệch về phía {(diff > 0 ? "cao hơn" : "thấp hơn")} đường trung tâm. Thường do đèn chiếu LED camera suy hao quang thông, hoặc nhiệt độ lò SMT/áp lực keo bị dịch chuyển.";
                        recommendation = $"Kiểm tra cường độ đèn VP và vệ sinh thấu kính camera tại trạm {key.Machine}. Hiệu chuẩn lại đồ gá.";
                        break;
                    case 3:
                        severity = "Warning";
                        direction = diff > 0 ? "Increasing" : "Decreasing";
                        rootCause = $"Xu hướng trượt liên tục (Trend): 6 điểm đo liên tiếp {(diff > 0 ? "tăng dần" : "giảm dần")}. Cảnh báo hao mòn đầu gá, bám bụi trên kính quang học hoặc giảm tuổi thọ bóng đèn LED.";
                        recommendation = "Bảo trì phòng ngừa: Lau sạch bề mặt quang học và kiểm tra áp suất cấp trước khi sinh ra hàng ngàn sản phẩm Overkill.";
                        break;
                    default:
                        severity = "Info";
                        direction = diff > 0 ? "Increasing" : "Decreasing";
                        rootCause = $"{v.RuleName}: Dao động đặc tính quang học bất thường trong 10 mẫu gần nhất.";
                        recommendation = "Theo dõi thêm các lô sản phẩm tiếp theo để xác định nguy cơ phát sinh lỗi.";
                        break;
                }

                alerts.Add(new SpcDriftAlertDto(
                    Guid.NewGuid(),
                    severity,
                    key.Machine,
                    key.Line,
                    key.Feature,
                    v.RuleId,
                    v.RuleName,
                    Math.Round(mean, 2),
                    Math.Round(currentVal, 2),
                    Math.Round(sigma, 2),
                    direction,
                    rootCause,
                    recommendation,
                    ordered[pointIdx].Time,
                    sampleKey
                ));
            }

            // Estimate Cpk using natural tolerance limits if USL/LSL not explicit
            // Assume 6-sigma process capability window
            var usl = mean + 4.0 * sigma;
            var lsl = Math.Max(0, mean - 4.0 * sigma);
            var cpu = (usl - mean) / (3.0 * sigma);
            var cpl = (mean - lsl) / (3.0 * sigma);
            var cpk = Math.Min(cpu, cpl);
            var cp = (usl - lsl) / (6.0 * sigma);

            string capStatus;
            if (cpk >= 1.67) capStatus = "WorldClass";
            else if (cpk >= 1.33) capStatus = "Adequate";
            else if (cpk >= 1.0) capStatus = "Risky";
            else capStatus = "Incapable";

            benchmarks.Add(new MachineCpkBenchmarkDto(
                key.Machine,
                key.Line,
                key.Feature,
                ordered.Count,
                Math.Round(mean, 2),
                Math.Round(sigma, 2),
                Math.Round(cp, 2),
                Math.Round(cpk, 2),
                capStatus,
                hasActiveDrift,
                violations.Count
            ));
        }

        // Sort alerts by severity (Critical first, then Warning, then Info)
        var sortedAlerts = alerts
            .OrderBy(a => a.Severity == "Critical" ? 0 : a.Severity == "Warning" ? 1 : 2)
            .ThenByDescending(a => a.DetectedAtUtc)
            .Take(50)
            .ToList();

        var sortedBenchmarks = benchmarks
            .OrderByDescending(b => b.HasActiveDrift)
            .ThenBy(b => b.Cpk ?? 0)
            .Take(30)
            .ToList();

        var criticalCount = sortedAlerts.Count(a => a.Severity == "Critical");
        var warningCount = sortedAlerts.Count(a => a.Severity == "Warning");
        var infoCount = sortedAlerts.Count(a => a.Severity == "Info");

        var response = new SpcEarlyWarningDashboardResponse(
            recentDefects.Count,
            sortedAlerts.Count,
            criticalCount,
            warningCount,
            infoCount,
            sortedAlerts,
            sortedBenchmarks,
            DateTimeOffset.UtcNow
        );

        Cache[cacheKey] = (DateTimeOffset.UtcNow.AddSeconds(30), response);
        return response;
    }
}
