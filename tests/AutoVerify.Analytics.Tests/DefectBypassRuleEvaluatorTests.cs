using AutoVerify.Api.Services;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class DefectBypassRuleEvaluatorTests
{
    private static RawDefectRow CreateRow(string? judge, string defectName, string? defectType = null) =>
        new(
            KeyId: "key-1",
            DefectTable: "tbl_defect",
            DefectName: defectName,
            DefectType: defectType ?? "Standard",
            ComponentName: null,
            CoordinateX: null,
            CoordinateY: null,
            Severity: "Major",
            Judge: judge!,
            ImageUri: null
        );

    [Fact]
    public void ShouldBypass_WhenRuleDisabledOrNull_ReturnsFalseExceptLegacyFD()
    {
        var rowNormal = CreateRow("OK", "noise");
        var rowFD = CreateRow("FD", "noise");

        Assert.False(DefectBypassRuleEvaluator.ShouldBypass(rowNormal, (DefectBypassRuleDto?)null));
        Assert.False(DefectBypassRuleEvaluator.ShouldBypass(rowNormal, new DefectBypassRuleDto(Enabled: false)));

        // Legacy behavior when rule is null or disabled: FD is bypassed
        Assert.True(DefectBypassRuleEvaluator.ShouldBypass(rowFD, (DefectBypassRuleDto?)null));
    }

    [Fact]
    public void ShouldBypass_CompoundRule_NoDefectAndNameNotIn_ReturnsTrueWhenMatching()
    {
        // User's specific rule: no defect AND name NOT IN ('FD','filter')
        var rule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "in",
                Value: null,
                Values: new[] { "OK", "PASS", "0" }
            ),
            NameCondition: new DefectBypassNameConditionDto(
                Field: "defectName",
                Operator: "not_in",
                Values: new[] { "FD", "filter" }
            ),
            CaseInsensitive: true,
            NullPolicy: "PreserveRow"
        );

        // Case 1: judge=OK, defectName=normal_noise -> matches no_defect (OK in list) AND name NOT IN (FD, filter) -> Bypass = TRUE
        var row1 = CreateRow("OK", "normal_noise");
        Assert.True(DefectBypassRuleEvaluator.ShouldBypass(row1, rule));

        // Case 2: judge=OK, defectName=FD -> matches no_defect, but name is in (FD, filter), so NOT IN is false -> Bypass = FALSE
        var row2 = CreateRow("OK", "FD");
        Assert.False(DefectBypassRuleEvaluator.ShouldBypass(row2, rule));

        // Case 3: judge=NG, defectName=normal_noise -> no_defect is false -> Bypass = FALSE
        var row3 = CreateRow("NG", "normal_noise");
        Assert.False(DefectBypassRuleEvaluator.ShouldBypass(row3, rule));
    }

    [Fact]
    public void ShouldBypass_WithNullPolicyPreserveRow_WhenFieldsNull_ReturnsFalse()
    {
        var rule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "equals",
                Value: "OK",
                Values: null
            ),
            NameCondition: null,
            CaseInsensitive: true,
            NullPolicy: "PreserveRow"
        );

        var row = CreateRow(null, "normal"); // null judge
        Assert.False(DefectBypassRuleEvaluator.ShouldBypass(row, rule));
    }

    [Fact]
    public void ShouldBypass_WithNullPolicyMatchAsNoDefect_WhenNoDefectFieldNull_MatchesCondition()
    {
        var rule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "equals",
                Value: "OK",
                Values: null
            ),
            NameCondition: null,
            CaseInsensitive: true,
            NullPolicy: "MatchAsNoDefect"
        );

        var row = CreateRow(null, "normal");
        Assert.True(DefectBypassRuleEvaluator.ShouldBypass(row, rule));
    }

    [Fact]
    public void ValidateRule_ReturnsErrorsForInvalidField()
    {
        var invalidRule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "unsupported_field_xyz",
                Operator: "equals",
                Value: "OK",
                Values: null
            ),
            NameCondition: new DefectBypassNameConditionDto(
                Field: "defectName",
                Operator: "NOT IN",
                Values: new[] { "FD" }
            ),
            CaseInsensitive: true,
            NullPolicy: "PreserveRow"
        );

        var (isValid, errorMessage) = DefectBypassRuleValidator.Validate(invalidRule);
        Assert.False(isValid);
        Assert.NotNull(errorMessage);
        Assert.Contains("unsupported_field_xyz", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateRule_AcceptsOrConjunctionWhenEnabled()
    {
        var rule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "OR",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "equals",
                Value: "OK",
                Values: null
            ),
            NameCondition: new DefectBypassNameConditionDto(
                Field: "defectName",
                Operator: "not_in",
                Values: new[] { "FD" }
            )
        );

        var (isValid, errorMessage) = DefectBypassRuleValidator.Validate(rule);
        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void ShouldBypass_OrRule_ReturnsTrueWhenEitherConditionMatches()
    {
        var rule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "OR",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "equals",
                Value: "OK",
                Values: null
            ),
            NameCondition: new DefectBypassNameConditionDto(
                Field: "defectName",
                Operator: "equals",
                Values: new[] { "ReferenceMark" }
            )
        );

        Assert.True(DefectBypassRuleEvaluator.ShouldBypass(CreateRow("OK", "normal"), rule));
        Assert.True(DefectBypassRuleEvaluator.ShouldBypass(CreateRow("NG", "ReferenceMark"), rule));
        Assert.False(DefectBypassRuleEvaluator.ShouldBypass(CreateRow("NG", "normal"), rule));
    }

    [Fact]
    public void ValidateRule_UnaryOperators_MustNotContainValues()
    {
        var ruleWithValue = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "is_null",
                Value: "unexpected_value",
                Values: null
            )
        );

        var (isValid, errorMessage) = DefectBypassRuleValidator.Validate(ruleWithValue);
        Assert.False(isValid);
        Assert.Contains("does not accept a comparison value", errorMessage);
    }

    [Fact]
    public void ValidateRule_ListLimits_Enforced()
    {
        // Value exceeding 100 chars
        var longValueRule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "equals",
                Value: new string('A', 101),
                Values: null
            ),
            NameCondition: new DefectBypassNameConditionDto(
                Field: "defectName",
                Operator: "not_in",
                Values: new[] { "FD" }
            )
        );
        var (isValLong, errLong) = DefectBypassRuleValidator.Validate(longValueRule);
        Assert.False(isValLong);
        Assert.Contains("maximum length of 100", errLong);

        // List exceeding 50 items
        var tooManyItems = Enumerable.Range(1, 51).Select(i => $"Val_{i}").ToArray();
        var tooManyRule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "equals",
                Value: "OK",
                Values: null
            ),
            NameCondition: new DefectBypassNameConditionDto(
                Field: "defectName",
                Operator: "not_in",
                Values: tooManyItems
            )
        );
        var (isValMany, errMany) = DefectBypassRuleValidator.Validate(tooManyRule);
        Assert.False(isValMany);
        Assert.Contains("maximum limit of 50 items", errMany);
    }

    [Fact]
    public void ShouldBypass_UnaryOperators_EvaluatesAccurately()
    {
        var isNullRule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "is_null",
                Value: null,
                Values: null
            ),
            NameCondition: null
        );

        var rowNull = CreateRow(null, "normal");
        var rowNotNull = CreateRow("OK", "normal");
        Assert.True(DefectBypassRuleEvaluator.ShouldBypass(rowNull, isNullRule));
        Assert.False(DefectBypassRuleEvaluator.ShouldBypass(rowNotNull, isNullRule));

        var isNotEmptyRule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "is_not_empty",
                Value: null,
                Values: null
            )
        );
        Assert.True(DefectBypassRuleEvaluator.ShouldBypass(rowNotNull, isNotEmptyRule));
        Assert.False(DefectBypassRuleEvaluator.ShouldBypass(rowNull, isNotEmptyRule));
    }

    [Fact]
    public void ShouldBypass_PreserveRowNullPolicy_NullNameNeverMatchesNotIn()
    {
        // When defectName is null, default PreserveRow must NOT satisfy 'not_in'
        // Therefore, rows with missing defect name are preserved (bypass = false)
        var rule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "equals",
                Value: "OK",
                Values: null
            ),
            NameCondition: new DefectBypassNameConditionDto(
                Field: "defectName",
                Operator: "not_in",
                Values: new[] { "FD", "filter" }
            ),
            NullPolicy: "PreserveRow"
        );

        var rowWithNullName = CreateRow("OK", null!);
        Assert.False(DefectBypassRuleEvaluator.ShouldBypass(rowWithNullName, rule));
    }

    [Fact]
    public void ShouldBypass_CustomPredicate_DoesNotAssumeJudgeEqualsFD()
    {
        // Custom predicate where DefectType indicates "no defect" (e.g., "Reference")
        // and DefectName NOT IN ("CriticalMark", "Border")
        var rule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "defectType",
                Operator: "equals",
                Value: "Reference",
                Values: null
            ),
            NameCondition: new DefectBypassNameConditionDto(
                Field: "defectName",
                Operator: "not_in",
                Values: new[] { "CriticalMark", "Border" }
            ),
            NullPolicy: "PreserveRow"
        );

        // Row with DefectType = Reference, DefectName = AlignMark -> Bypass
        var row1 = CreateRow("NG", "AlignMark", defectType: "Reference");
        Assert.True(DefectBypassRuleEvaluator.ShouldBypass(row1, rule));

        // Row with DefectType = Reference, DefectName = CriticalMark -> Do NOT bypass (it's excluded)
        var row2 = CreateRow("NG", "CriticalMark", defectType: "Reference");
        Assert.False(DefectBypassRuleEvaluator.ShouldBypass(row2, rule));

        // Row with DefectType = RealDefect, DefectName = AlignMark -> Do NOT bypass (no-defect predicate false)
        var row3 = CreateRow("NG", "AlignMark", defectType: "RealDefect");
        Assert.False(DefectBypassRuleEvaluator.ShouldBypass(row3, rule));
    }

    [Fact]
    public void FormatSummary_BoundedLength()
    {
        var manyValues = Enumerable.Range(1, 50).Select(i => $"VeryLongValueName_{i:000}").ToArray();
        var rule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "in",
                Value: null,
                Values: manyValues
            ),
            NameCondition: new DefectBypassNameConditionDto(
                Field: "defectName",
                Operator: "not_in",
                Values: manyValues
            )
        );

        var summary = DefectBypassRuleEvaluator.FormatSummary(rule);
        Assert.NotNull(summary);
        Assert.True(summary.Length <= 512, $"Summary length {summary.Length} exceeded 512 characters.");
        Assert.Contains("...", summary);
    }

    [Fact]
    public void ValidateRule_OnlyJudgeCondition_IsValid()
    {
        var rule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "equals",
                Value: "G",
                Values: null
            ),
            NameCondition: null
        );

        var (isValid, errorMessage) = DefectBypassRuleValidator.Validate(rule);
        Assert.True(isValid);
        Assert.Null(errorMessage);

        var summary = DefectBypassRuleEvaluator.FormatSummary(rule);
        Assert.Equal("Skip when [judge = 'G']", summary);
    }

    [Fact]
    public void ShouldBypass_OnlyJudgeG_BypassesGAndKeepsNG()
    {
        var rule = new DefectBypassRuleDto(
            Enabled: true,
            Conjunction: "AND",
            NoDefectCondition: new DefectBypassConditionDto(
                Field: "judge",
                Operator: "equals",
                Value: "G",
                Values: null
            ),
            NameCondition: null
        );

        Assert.True(DefectBypassRuleEvaluator.ShouldBypass(CreateRow("G", "Scratch"), rule));
        Assert.True(DefectBypassRuleEvaluator.ShouldBypass(CreateRow("g", "Particle"), rule));
        Assert.False(DefectBypassRuleEvaluator.ShouldBypass(CreateRow("NG", "Scratch"), rule));
        Assert.False(DefectBypassRuleEvaluator.ShouldBypass(CreateRow("N", "Scratch"), rule));
    }

    [Fact]
    public void BuildSqlFilterClause_WhenRuleDisabledOrNull_GeneratesDefaultExcludeGAndFD()
    {
        var defectTable = new DefectTableMapping("mcl_defect", JudgeColumn: "judge");
        var sql = DefectBypassRuleEvaluator.BuildSqlFilterClause(defectTable, null);

        Assert.Contains("`judge`", sql);
        Assert.Contains("NOT IN ('G', 'FD')", sql);
    }

    [Fact]
    public void BuildSqlFilterClause_WhenJudgeG_GeneratesSqlExcludingG()
    {
        var defectTable = new DefectTableMapping("mcl_defect", JudgeColumn: "judge");
        var rule = new DefectBypassRuleDto(
            Enabled: true,
            NoDefectCondition: new DefectBypassConditionDto("Judge", "equals", "G")
        );
        var sql = DefectBypassRuleEvaluator.BuildSqlFilterClause(defectTable, rule);

        Assert.Contains("`judge`", sql);
        Assert.Contains("<> 'G'", sql);
    }

    [Fact]
    public void BuildSqlFilterClause_WhenJudgeMultipleValues_GeneratesSqlNotIn()
    {
        var defectTable = new DefectTableMapping("mcl_defect", JudgeColumn: "judge");
        var rule = new DefectBypassRuleDto(
            Enabled: true,
            NoDefectCondition: new DefectBypassConditionDto("Judge", "in", Values: new[] { "G", "OK", "PASS" })
        );
        var sql = DefectBypassRuleEvaluator.BuildSqlFilterClause(defectTable, rule);

        Assert.Contains("`judge`", sql);
        Assert.Contains("NOT IN ('G', 'OK', 'PASS')", sql);
    }

    [Fact]
    public void BuildSqlFilterClause_WhenSpecJudgeConfigured_PushesDownToSpecJudgeColumn()
    {
        var defectTable = new DefectTableMapping("mcl_defect", JudgeColumn: "judge", SpecJudgeColumn: "spec_judge");
        var rule = new DefectBypassRuleDto(
            Enabled: true,
            NoDefectCondition: new DefectBypassConditionDto("SpecJudge", "equals", "G")
        );
        var sql = DefectBypassRuleEvaluator.BuildSqlFilterClause(defectTable, rule);

        Assert.Contains("`spec_judge`", sql);
        Assert.Contains("<> 'G'", sql);
    }
}
