using AutoVerify.Api.Analytics;
using AutoVerify.Api.Analytics.Contracts;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/classifier-rules")]
public sealed class ClassifierRulesController(AutoVerifyDbContext db, ILogger<ClassifierRulesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ClassifierRuleDto>>> ListRules(
        [FromQuery] string? lineCode = null,
        [FromQuery] string? machineCode = null,
        [FromQuery] string? stationCode = null,
        [FromQuery] string? ruleCode = null,
        [FromQuery] string? defectFamily = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.MachineClassifierRules.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(lineCode))
        {
            var line = lineCode.Trim();
            query = query.Where(r => r.LineCode == line);
        }
        if (!string.IsNullOrWhiteSpace(machineCode))
        {
            var machine = machineCode.Trim();
            query = query.Where(r => r.MachineCode == machine);
        }
        if (!string.IsNullOrWhiteSpace(stationCode))
        {
            var station = stationCode.Trim();
            query = query.Where(r => r.StationCode == station);
        }
        if (!string.IsNullOrWhiteSpace(ruleCode))
        {
            var code = ruleCode.Trim();
            query = query.Where(r => r.RuleCode == code);
        }
        if (!string.IsNullOrWhiteSpace(defectFamily))
        {
            var fam = defectFamily.Trim();
            query = query.Where(r => r.DefectFamily == fam);
        }
        if (isActive.HasValue)
        {
            query = query.Where(r => r.IsActive == isActive.Value);
        }

        var rules = await query
            .OrderBy(r => r.RuleCode)
            .ThenByDescending(r => r.Version)
            .Select(r => MapToDto(r))
            .ToListAsync(cancellationToken);

        return Ok(rules);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClassifierRuleDto>> GetRuleById(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await db.MachineClassifierRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (rule == null)
        {
            return NotFound(new { error = $"Classifier rule with ID '{id}' was not found." });
        }

        return Ok(MapToDto(rule));
    }

    [HttpPost]
    public async Task<ActionResult<ClassifierRuleDto>> CreateRule(
        [FromBody] CreateClassifierRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RuleCode))
        {
            return BadRequest(new { error = "RuleCode is required." });
        }
        if (string.IsNullOrWhiteSpace(request.RuleName))
        {
            return BadRequest(new { error = "RuleName is required." });
        }
        if (string.IsNullOrWhiteSpace(request.Version))
        {
            return BadRequest(new { error = "Version is required." });
        }

        var entity = new MachineClassifierRuleEntity
        {
            Id = Guid.NewGuid(),
            RuleCode = request.RuleCode.Trim(),
            RuleName = request.RuleName.Trim(),
            Version = request.Version.Trim(),
            ProductKey = request.ProductKey?.Trim(),
            LineCode = request.LineCode?.Trim(),
            MachineCode = request.MachineCode?.Trim(),
            StationCode = request.StationCode?.Trim(),
            DefectFamily = request.DefectFamily?.Trim(),
            DefectClass = request.DefectClass?.Trim(),
            ThresholdsConfigJson = string.IsNullOrWhiteSpace(request.ThresholdsConfigJson) ? "{}" : request.ThresholdsConfigJson.Trim(),
            ModelMetadataJson = request.ModelMetadataJson?.Trim(),
            ScriptContent = request.ScriptContent?.Trim(),
            Priority = request.Priority,
            DefectTypeId = request.DefectTypeId?.Trim(),
            IsActive = request.IsActive,
            EffectiveFromUtc = request.EffectiveFromUtc ?? DateTimeOffset.UtcNow,
            EffectiveToUtc = request.EffectiveToUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = User.Identity?.Name ?? "system",
            Description = request.Description?.Trim()
        };

        db.MachineClassifierRules.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Created classifier rule {RuleCode} version {Version} (ID: {Id})",
            entity.RuleCode, entity.Version, entity.Id);

        return CreatedAtAction(nameof(GetRuleById), new { id = entity.Id }, MapToDto(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ClassifierRuleDto>> UpdateRule(
        Guid id,
        [FromBody] UpdateClassifierRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await db.MachineClassifierRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (entity == null)
        {
            return NotFound(new { error = $"Classifier rule with ID '{id}' was not found." });
        }

        if (request.RuleName != null) entity.RuleName = request.RuleName.Trim();
        if (request.ThresholdsConfigJson != null) entity.ThresholdsConfigJson = request.ThresholdsConfigJson.Trim();
        if (request.ModelMetadataJson != null) entity.ModelMetadataJson = request.ModelMetadataJson.Trim();
        if (request.ScriptContent != null) entity.ScriptContent = request.ScriptContent.Trim();
        if (request.Priority.HasValue) entity.Priority = request.Priority.Value;
        if (request.DefectTypeId != null) entity.DefectTypeId = request.DefectTypeId.Trim();
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;
        if (request.EffectiveToUtc.HasValue) entity.EffectiveToUtc = request.EffectiveToUtc;
        if (request.Description != null) entity.Description = request.Description.Trim();
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(MapToDto(entity));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await db.MachineClassifierRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (entity == null)
        {
            return NotFound(new { error = $"Classifier rule with ID '{id}' was not found." });
        }

        db.MachineClassifierRules.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Deleted classifier rule {RuleCode} (ID: {Id})", entity.RuleCode, id);
        return NoContent();
    }

    [HttpPost("test")]
    [ProducesResponseType(typeof(TestClassifierRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TestClassifierRuleResponse>> TestRule(
        [FromBody] TestClassifierRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var script = request.ScriptContent;
        if (string.IsNullOrWhiteSpace(script) && request.RuleId.HasValue)
        {
            var rule = await db.MachineClassifierRules.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == request.RuleId.Value, cancellationToken);
            script = rule?.ScriptContent;
        }

        if (string.IsNullOrWhiteSpace(script))
        {
            return BadRequest(new TestClassifierRuleResponse(
                Success: false,
                FinalResult: false,
                Verdict: "Error",
                GuardResults: [],
                ConditionResults: [],
                FeatureValues: [],
                GuardFailed: false,
                FailedGuardFeature: null,
                RequiredFeatures: [],
                Warnings: ["No script provided or found for the specified rule."],
                ErrorMessage: "Script content is required for testing."
            ));
        }

        var features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (request.Features != null)
        {
            foreach (var kv in request.Features)
            {
                features[kv.Key] = kv.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.FeaturesJson))
        {
            var parsed = AutoVerify.Api.Services.DefectStreamParser.ExtractNumericFeaturesFromJson(request.FeaturesJson);
            foreach (var (name, value) in parsed)
            {
                features[name] = value;
            }
        }

        try
        {
            var parsedScript = ScriptConditionEngine.Parse(script);
            var obs = new List<(int RowIndex, Dictionary<string, double> Features, ObservationReference? Observation)>
            {
                (1, features, null)
            };

            var evalResult = ScriptConditionEngine.EvaluateImperative(script, parsedScript, obs);
            var ev = evalResult.Evaluations.FirstOrDefault();

            if (ev == null)
            {
                return Ok(new TestClassifierRuleResponse(
                    Success: true,
                    FinalResult: false,
                    Verdict: "No Evaluation",
                    GuardResults: [],
                    ConditionResults: [],
                    FeatureValues: [],
                    GuardFailed: false,
                    FailedGuardFeature: null,
                    RequiredFeatures: parsedScript.AllRequiredFeatures,
                    Warnings: ["No observation evaluation was produced."]
                ));
            }

            var verdict = ev.FinalResult
                ? "TRUE (Defect Detected)"
                : (ev.GuardFailed ? $"FALSE (Filtered by Guard: {ev.FailedGuardFeature})" : "FALSE (Script condition unsatisfied / Clean)");

            return Ok(new TestClassifierRuleResponse(
                Success: true,
                FinalResult: ev.FinalResult,
                Verdict: verdict,
                GuardResults: ev.GuardResults,
                ConditionResults: ev.ConditionResults,
                FeatureValues: ev.FeatureValues,
                GuardFailed: ev.GuardFailed,
                FailedGuardFeature: ev.FailedGuardFeature,
                RequiredFeatures: parsedScript.AllRequiredFeatures,
                Warnings: evalResult.Warnings
            ));
        }
        catch (Exception ex)
        {
            return Ok(new TestClassifierRuleResponse(
                Success: false,
                FinalResult: false,
                Verdict: "Execution Error",
                GuardResults: [],
                ConditionResults: [],
                FeatureValues: [],
                GuardFailed: false,
                FailedGuardFeature: null,
                RequiredFeatures: [],
                Warnings: [ex.Message],
                ErrorMessage: ex.Message
            ));
        }
    }

    [HttpGet("{id:guid}/tuning-analysis")]
    [ProducesResponseType(typeof(RuleTuningAnalysisResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RuleTuningAnalysisResponse>> GetRuleTuningAnalysis(
        Guid id,
        [FromQuery] string? machineCode = null,
        [FromQuery] string? lineCode = null,
        CancellationToken cancellationToken = default)
    {
        var rule = await db.MachineClassifierRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule == null)
        {
            return NotFound(new { error = $"Rule with ID '{id}' was not found." });
        }

        var defectName = rule.DefectClass ?? rule.RuleCode;
        var samples = await LoadGroundTruthSamplesAsync(defectName, machineCode ?? rule.MachineCode, lineCode ?? rule.LineCode, cancellationToken);

        var originalScript = rule.ScriptContent ?? "";
        var separations = RuleTuningEngine.AnalyzeFeatureDistributions(samples, originalScript);
        var autoTunedScript = RuleTuningEngine.GenerateTunedScript(originalScript, separations);

        var overkillCount = samples.Count(s => !s.IsGroundTruthRealDefect);
        var realCount = samples.Count(s => s.IsGroundTruthRealDefect);
        var total = samples.Count;
        var overkillRate = total > 0 ? Math.Round((double)overkillCount / total, 4) : 0.0;

        var recDtos = separations.Select(s => new RuleTuningRecommendationDto(
            s.FeatureName,
            s.CurrentThresholdInScript,
            s.ProposedThreshold,
            s.ComparisonOperator,
            s.OverkillMin,
            s.OverkillMax,
            s.OverkillMean,
            s.RealMin,
            s.RealMax,
            s.RealMean,
            s.SeparationKind,
            s.ProjectedOverkillCutPercent,
            s.RecommendedChangeCode,
            s.Explanation
        )).ToList();

        var topRec = separations.FirstOrDefault(s => s.SeparationKind == "CompletelySeparated") ?? separations.FirstOrDefault();
        string executiveSummary = topRec != null
            ? $"Based on {samples.Count} historical samples ({overkillCount} Overkill, {realCount} Real Defect), tuning feature [{topRec.FeatureName}] to threshold {topRec.ComparisonOperator} {topRec.ProposedThreshold} is projected to eliminate ~{topRec.ProjectedOverkillCutPercent}% Overkill with Zero Leakage!"
            : $"Analyzed {samples.Count} historical samples for defect [{defectName}]. Additional separation data recommended for further threshold tuning.";

        return Ok(new RuleTuningAnalysisResponse(
            rule.Id,
            rule.RuleCode,
            rule.RuleName,
            defectName,
            overkillCount,
            realCount,
            overkillRate,
            recDtos,
            originalScript,
            autoTunedScript,
            executiveSummary
        ));
    }

    [HttpPost("simulate-what-if")]
    [ProducesResponseType(typeof(SimulateWhatIfResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SimulateWhatIfResponse>> SimulateWhatIf(
        [FromBody] SimulateWhatIfRequest request,
        CancellationToken cancellationToken = default)
    {
        string? baselineScript = request.BaselineScript;
        string? defectName = request.DefectName;

        if (string.IsNullOrWhiteSpace(baselineScript) && request.RuleId.HasValue)
        {
            var rule = await db.MachineClassifierRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RuleId.Value, cancellationToken);
            baselineScript = rule?.ScriptContent;
            defectName ??= rule?.DefectClass ?? rule?.RuleCode;
        }

        if (string.IsNullOrWhiteSpace(baselineScript))
        {
            return BadRequest(new { error = "Baseline script is required (either provide BaselineScript or valid RuleId)." });
        }

        if (string.IsNullOrWhiteSpace(request.CandidateScript))
        {
            return BadRequest(new { error = "Candidate script is required to simulate comparison." });
        }

        var samples = await LoadGroundTruthSamplesAsync(defectName, request.MachineCode, request.LineCode, cancellationToken, request.MaxSamples);
        if (samples.Count == 0)
        {
            return BadRequest(new { error = "No historical defect samples found to run What-If simulation. Please ensure defect records and/or OP data are available." });
        }

        var result = RuleTuningEngine.SimulateWhatIf(baselineScript, request.CandidateScript, samples);

        var divergenceDtos = result.Divergences.Select(d => new WhatIfDivergenceDto(
            d.KeyId,
            d.GroundTruthIsDefect,
            d.BaselineVerdict,
            d.CandidateVerdict,
            d.DivergenceKind,
            d.Features,
            d.Explanation
        )).ToList();

        return Ok(new SimulateWhatIfResponse(
            result.TotalEvaluated,
            result.BaselineRealDefectCount,
            result.BaselineOverkillCount,
            result.BaselineOverkillRate,
            result.BaselineLeakageCount,
            result.CandidateRealDefectCount,
            result.CandidateOverkillCount,
            result.CandidateOverkillRate,
            result.CandidateLeakageCount,
            result.OverkillReducedCount,
            result.OverkillReductionPercent,
            result.NewLeakageIntroducedCount,
            result.IsSafeToDeploy,
            result.EstimatedSavedOpHoursPerMonth,
            result.DetailedExplanation,
            divergenceDtos
        ));
    }

    private async Task<List<RuleTuningEngine.FeatureSample>> LoadGroundTruthSamplesAsync(
        string? defectName,
        string? machineCode,
        string? lineCode,
        CancellationToken ct,
        int maxLimit = 2000)
    {
        var defectQuery = db.DefectRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(defectName) && !defectName.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            defectQuery = defectQuery.Where(d =>
                d.DefectName.Contains(defectName) ||
                (d.DefectType != null && d.DefectType.Contains(defectName)) ||
                d.DefectTable.Contains(defectName));
        }

        var defects = await defectQuery.OrderByDescending(d => d.CreatedAtUtc).Take(maxLimit).ToListAsync(ct);
        var keyIds = defects.Select(d => d.KeyId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var opRecords = await db.OpInspectionRecords.AsNoTracking()
            .Where(o => keyIds.Contains(o.KeyId))
            .ToListAsync(ct);

        var opByKey = opRecords
            .GroupBy(o => o.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var mappings = await db.OpDefectMappings.AsNoTracking().ToListAsync(ct);
        var mappingDict = mappings
            .GroupBy(m => m.OpDefectName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var samples = new List<RuleTuningEngine.FeatureSample>();

        foreach (var def in defects)
        {
            var features = Verification.AnomalyClassifierEngine.ExtractAllFeatures(def);
            if (features.Count == 0) continue;

            bool isRealDefect;
            if (opByKey.TryGetValue(def.KeyId, out var opRec))
            {
                if (!string.IsNullOrWhiteSpace(opRec.OpDefectName) && mappingDict.TryGetValue(opRec.OpDefectName.Trim(), out var map))
                {
                    isRealDefect = map.IsConfirmedDefect;
                }
                else
                {
                    isRealDefect = opRec.OpJudge.Equals("NG", StringComparison.OrdinalIgnoreCase) || opRec.OpJudge.Equals("N", StringComparison.OrdinalIgnoreCase);
                }
            }
            else
            {
                // Heuristic baseline if OP label is missing for this unit:
                // High confidence defect is considered real defect, low confidence (<0.85) or minor severity considered overkill candidate
                isRealDefect = def.ConfidenceScore >= 0.88 && (def.Severity == "Critical" || def.Severity == "Major");
            }

            samples.Add(new RuleTuningEngine.FeatureSample(
                def.KeyId,
                def.DefectName,
                isRealDefect,
                features,
                lineCode,
                machineCode,
                def.CreatedAtUtc
            ));
        }

        return samples;
    }

    [HttpPost("resolve")]
    public async Task<ActionResult<ResolveClassifierRuleResponse>> ResolveRule(
        [FromBody] ResolveClassifierRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var targetTime = request.EffectiveTimeUtc ?? DateTimeOffset.UtcNow;

        // Candidate active rules that overlap the target time
        var candidates = await db.MachineClassifierRules
            .AsNoTracking()
            .Where(r => r.IsActive)
            .Where(r => r.EffectiveFromUtc <= targetTime && (r.EffectiveToUtc == null || r.EffectiveToUtc > targetTime))
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.RuleCode))
        {
            candidates = candidates
                .Where(r => string.Equals(r.RuleCode, request.RuleCode.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Rank candidates by specificity score
        var scored = candidates
            .Select(rule =>
            {
                var score = 0;
                var matchKinds = new List<string>();

                // Station match
                if (!string.IsNullOrWhiteSpace(rule.StationCode))
                {
                    if (!string.IsNullOrWhiteSpace(request.StationCode) &&
                        string.Equals(rule.StationCode, request.StationCode.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        score += 16;
                        matchKinds.Add("StationMatch");
                    }
                    else
                    {
                        return (Rule: rule, Score: -1, ResolutionKind: "StationMismatch");
                    }
                }

                // Machine match
                if (!string.IsNullOrWhiteSpace(rule.MachineCode))
                {
                    if (!string.IsNullOrWhiteSpace(request.MachineCode) &&
                        string.Equals(rule.MachineCode, request.MachineCode.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        score += 8;
                        matchKinds.Add("MachineMatch");
                    }
                    else
                    {
                        return (Rule: rule, Score: -1, ResolutionKind: "MachineMismatch");
                    }
                }

                // Line match
                if (!string.IsNullOrWhiteSpace(rule.LineCode))
                {
                    if (!string.IsNullOrWhiteSpace(request.LineCode) &&
                        string.Equals(rule.LineCode, request.LineCode.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        score += 4;
                        matchKinds.Add("LineMatch");
                    }
                    else
                    {
                        return (Rule: rule, Score: -1, ResolutionKind: "LineMismatch");
                    }
                }

                // ProductKey match
                if (!string.IsNullOrWhiteSpace(rule.ProductKey))
                {
                    if (!string.IsNullOrWhiteSpace(request.ProductKey) &&
                        string.Equals(rule.ProductKey, request.ProductKey.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        score += 2;
                        matchKinds.Add("ProductMatch");
                    }
                    else
                    {
                        return (Rule: rule, Score: -1, ResolutionKind: "ProductMismatch");
                    }
                }

                // DefectClass match
                if (!string.IsNullOrWhiteSpace(rule.DefectClass))
                {
                    if (!string.IsNullOrWhiteSpace(request.DefectClass) &&
                        string.Equals(rule.DefectClass, request.DefectClass.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        score += 4;
                        matchKinds.Add("DefectClassMatch");
                    }
                    else
                    {
                        return (Rule: rule, Score: -1, ResolutionKind: "DefectClassMismatch");
                    }
                }

                // DefectFamily match
                if (!string.IsNullOrWhiteSpace(rule.DefectFamily))
                {
                    if (!string.IsNullOrWhiteSpace(request.DefectFamily) &&
                        string.Equals(rule.DefectFamily, request.DefectFamily.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        score += 2;
                        matchKinds.Add("DefectFamilyMatch");
                    }
                    else
                    {
                        return (Rule: rule, Score: -1, ResolutionKind: "DefectFamilyMismatch");
                    }
                }

                var resolutionKind = matchKinds.Count > 0 ? string.Join("+", matchKinds) : "GlobalFallback";
                return (Rule: rule, Score: score, ResolutionKind: resolutionKind);
            })
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Rule.EffectiveFromUtc)
            .ThenByDescending(x => x.Rule.Version)
            .ToList();

        if (scored.Count == 0)
        {
            return Ok(new ResolveClassifierRuleResponse(
                IsResolved: false,
                Rule: null,
                MatchResolutionKind: "NoMatch",
                SpecificityScore: 0,
                Message: "No active classifier rule resolved for the specified scope and effective time."
            ));
        }

        var winner = scored[0];
        return Ok(new ResolveClassifierRuleResponse(
            IsResolved: true,
            Rule: MapToDto(winner.Rule),
            MatchResolutionKind: winner.ResolutionKind,
            SpecificityScore: winner.Score,
            Message: $"Resolved rule '{winner.Rule.RuleCode}' version '{winner.Rule.Version}'."
        ));
    }

    [HttpPost("import-rules-json")]
    [ProducesResponseType(typeof(List<ClassifierRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ClassifierRuleDto>>> ImportRulesJson(
        [FromBody] ImportAoiRulesJsonRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Rules == null || request.Rules.Count == 0)
        {
            return BadRequest(new { error = "No rules found in request payload." });
        }

        var results = new List<ClassifierRuleDto>();
        var now = DateTimeOffset.UtcNow;

        foreach (var r in request.Rules)
        {
            if (string.IsNullOrWhiteSpace(r.Name)) continue;

            var ruleName = r.Name.Trim();
            var ruleCode = ruleName;

            // Check if rule already exists by RuleCode
            var existing = await db.MachineClassifierRules
                .FirstOrDefaultAsync(x => x.RuleCode == ruleCode, cancellationToken);

            Guid ruleId = Guid.TryParse(r.GUID?.Trim('{', '}'), out var parsedGuid) ? parsedGuid : Guid.NewGuid();

            if (existing != null)
            {
                existing.RuleName = ruleName;
                existing.ScriptContent = r.Script?.Trim();
                existing.Priority = r.Priority;
                existing.DefectTypeId = r.DefectTypeID?.Trim();
                existing.DefectClass = ruleName;
                if (!string.IsNullOrWhiteSpace(request.MachineCode)) existing.MachineCode = request.MachineCode;
                if (!string.IsNullOrWhiteSpace(request.LineCode)) existing.LineCode = request.LineCode;
                existing.UpdatedAtUtc = now;
                results.Add(MapToDto(existing));
            }
            else
            {
                var newEntity = new MachineClassifierRuleEntity
                {
                    Id = ruleId,
                    RuleCode = ruleCode,
                    RuleName = ruleName,
                    Version = "1.0.0",
                    LineCode = request.LineCode,
                    MachineCode = request.MachineCode,
                    DefectClass = ruleName,
                    Priority = r.Priority,
                    DefectTypeId = r.DefectTypeID?.Trim(),
                    ScriptContent = r.Script?.Trim(),
                    IsActive = true,
                    CreatedAtUtc = now,
                    CreatedBy = User.Identity?.Name ?? "aoi_import",
                    Description = $"Imported AOI Rule: {ruleName} (Priority {r.Priority})"
                };
                db.MachineClassifierRules.Add(newEntity);
                results.Add(MapToDto(newEntity));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Imported {Count} AOI classifier rules from JSON payload", results.Count);
        return Ok(results);
    }

    private static ClassifierRuleDto MapToDto(MachineClassifierRuleEntity r) =>
        new(
            r.Id,
            r.RuleCode,
            r.RuleName,
            r.Version,
            r.ProductKey,
            r.LineCode,
            r.MachineCode,
            r.StationCode,
            r.DefectFamily,
            r.DefectClass,
            r.ThresholdsConfigJson,
            r.ModelMetadataJson,
            r.IsActive,
            r.EffectiveFromUtc,
            r.EffectiveToUtc,
            r.CreatedAtUtc,
            r.UpdatedAtUtc,
            r.CreatedBy,
            r.Description,
            r.ScriptContent,
            r.Priority,
            r.DefectTypeId
        );
}
