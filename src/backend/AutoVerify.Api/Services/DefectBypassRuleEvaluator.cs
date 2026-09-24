using System.Text.Json;
using System.Text.RegularExpressions;
using AutoVerify.Api.Persistence.Entities;

namespace AutoVerify.Api.Services;

public static class DefectBypassRuleConstants
{
    public static readonly HashSet<string> WhitelistedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Judge",
        "DefectName",
        "Name",
        "SpecJudge",
        "DefectType",
        "Severity",
        "Reason",
        "SpecName",
        "SpecReason"
    };

    public static readonly HashSet<string> WhitelistedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "equals",
        "not_equals",
        "is_null",
        "is_not_null",
        "is_empty",
        "is_not_empty",
        "is_null_or_empty",
        "in",
        "not_in"
    };

    public static readonly HashSet<string> WhitelistedNameOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "NOT IN",
        "IN",
        "EQUALS",
        "NOT EQUALS",
        "NOT_IN",
        "NOT_EQUALS"
    };

    public static readonly HashSet<string> WhitelistedConjunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND",
        "OR"
    };

    public static readonly HashSet<string> WhitelistedNullPolicies = new(StringComparer.OrdinalIgnoreCase)
    {
        "PreserveRow",
        "MatchAsNoDefect",
        "Strict"
    };

    private static readonly Regex IdentifierRegex = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    public static bool IsSafeIdentifier(string identifier) =>
        !string.IsNullOrWhiteSpace(identifier) && IdentifierRegex.IsMatch(identifier.Trim());
}

public static class DefectBypassRuleValidator
{
    public static (bool IsValid, string? ErrorMessage) Validate(DefectBypassRuleDto? rule)
    {
        if (rule is null || !rule.Enabled)
        {
            return (true, null);
        }

        var conjunction = string.IsNullOrWhiteSpace(rule.Conjunction) ? "AND" : rule.Conjunction.Trim();
        if (!DefectBypassRuleConstants.WhitelistedConjunctions.Contains(conjunction))
        {
            return (false, $"Invalid conjunction '{rule.Conjunction}'. Supported conjunctions: AND, OR.");
        }

        if (!string.IsNullOrWhiteSpace(rule.NullPolicy) &&
            !DefectBypassRuleConstants.WhitelistedNullPolicies.Contains(rule.NullPolicy.Trim()))
        {
            return (false, $"Invalid null policy '{rule.NullPolicy}'. Supported policies: PreserveRow, MatchAsNoDefect, Strict.");
        }

        // Require at least one condition (judge bypass or name exclusion)
        if (rule.NoDefectCondition is null && rule.NameCondition is null)
        {
            return (false, "At least one condition (no-defect judge or name exclusion) is required when bypass is enabled.");
        }

        // Validate NoDefectCondition if provided
        if (rule.NoDefectCondition is not null)
        {
            var noDefect = rule.NoDefectCondition;
        if (string.IsNullOrWhiteSpace(noDefect.Field) ||
            !DefectBypassRuleConstants.WhitelistedFields.Contains(noDefect.Field.Trim()) ||
            !DefectBypassRuleConstants.IsSafeIdentifier(noDefect.Field))
        {
            return (false, $"Invalid field '{noDefect.Field}' in 'no defect' condition. Allowed fields: {string.Join(", ", DefectBypassRuleConstants.WhitelistedFields)}.");
        }

        var op = noDefect.Operator?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(op) || !DefectBypassRuleConstants.WhitelistedOperators.Contains(op))
        {
            return (false, $"Invalid operator '{noDefect.Operator}' in 'no defect' condition. Allowed operators: {string.Join(", ", DefectBypassRuleConstants.WhitelistedOperators)}.");
        }

        if (op is "is_null" or "is_not_null" or "is_empty" or "is_not_empty" or "is_null_or_empty")
        {
            if (!string.IsNullOrWhiteSpace(noDefect.Value) || (noDefect.Values is not null && noDefect.Values.Any(v => !string.IsNullOrWhiteSpace(v))))
            {
                return (false, $"Unary operator '{op}' in 'no defect' condition does not accept a comparison value.");
            }
        }
        else if (op is "in" or "not_in")
        {
            if (noDefect.Values is null || noDefect.Values.Count == 0 || !noDefect.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
            {
                return (false, $"Operator '{op}' requires at least one non-empty value in 'no defect' condition.");
            }
            if (noDefect.Values.Count > 50)
            {
                return (false, "The 'no defect' condition values list exceeds the maximum limit of 50 items.");
            }
            foreach (var val in noDefect.Values)
            {
                if (val != null && val.Length > 100)
                {
                    return (false, "A value in the 'no defect' condition exceeds the maximum length of 100 characters.");
                }
            }
        }
        else if (op is "equals" or "not_equals")
        {
            var val = noDefect.Value ?? noDefect.Values?.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(val))
            {
                return (false, $"Operator '{op}' requires a non-empty comparison value in 'no defect' condition.");
            }
            if (val.Length > 100)
            {
                return (false, "Comparison value exceeds the maximum length of 100 characters.");
            }
        }
    }

        // Validate NameCondition if provided
        if (rule.NameCondition is not null)
        {
            var nameCond = rule.NameCondition;
        var nameField = string.IsNullOrWhiteSpace(nameCond.Field) ? "DefectName" : nameCond.Field.Trim();
        if (!DefectBypassRuleConstants.WhitelistedFields.Contains(nameField) ||
            !DefectBypassRuleConstants.IsSafeIdentifier(nameField))
        {
            return (false, $"Invalid field '{nameCond.Field}' in name condition. Allowed fields: {string.Join(", ", DefectBypassRuleConstants.WhitelistedFields)}.");
        }

        var nameOp = nameCond.Operator?.Trim().ToUpperInvariant() ?? "NOT IN";
        if (!DefectBypassRuleConstants.WhitelistedNameOperators.Contains(nameOp))
        {
            return (false, $"Invalid operator '{nameCond.Operator}' in name condition. Allowed operators: {string.Join(", ", DefectBypassRuleConstants.WhitelistedNameOperators)}.");
        }

        if (nameOp is "NOT IN" or "IN" or "NOT_IN")
        {
            if (nameCond.Values is null || nameCond.Values.Count == 0 || !nameCond.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
            {
                return (false, $"Operator '{nameOp}' in name condition requires at least one non-empty value in the excluded/included list.");
            }
            if (nameCond.Values.Count > 50)
            {
                return (false, "Name condition values list exceeds the maximum limit of 50 items.");
            }
            foreach (var val in nameCond.Values)
            {
                if (val != null && val.Length > 100)
                {
                    return (false, "A value in the name condition exceeds the maximum length of 100 characters.");
                }
            }
        }
        else if (nameOp is "EQUALS" or "NOT EQUALS" or "NOT_EQUALS")
        {
            var val = nameCond.Values?.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(val))
            {
                return (false, $"Operator '{nameOp}' in name condition requires a comparison value.");
            }
            if (val.Length > 100)
            {
                return (false, "A value in the name condition exceeds the maximum length of 100 characters.");
            }
        }
    }

        return (true, null);
    }
}

public static class DefectBypassRuleEvaluator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static DefectBypassRuleDto? ParseRule(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<DefectBypassRuleDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static string FormatSummary(DefectBypassRuleDto? rule)
    {
        if (rule is null || !rule.Enabled)
        {
            return "Disabled (Legacy Judge = 'FD' SQL pushdown active)";
        }

        var noDefectStr = rule.NoDefectCondition is not null ? FormatCondition(rule.NoDefectCondition) : null;
        var nameStr = rule.NameCondition is not null ? FormatNameCondition(rule.NameCondition) : null;
        var conj = string.Equals(rule.Conjunction?.Trim(), "OR", StringComparison.OrdinalIgnoreCase) ? "OR" : "AND";

        string summary;
        if (noDefectStr is not null && nameStr is not null)
        {
            summary = $"Skip when {noDefectStr} {conj} {nameStr}";
        }
        else if (noDefectStr is not null)
        {
            summary = $"Skip when {noDefectStr}";
        }
        else
        {
            summary = $"Skip when {nameStr}";
        }

        return summary.Length > 512 ? summary[..509] + "..." : summary;
    }

    private static string FormatCondition(DefectBypassConditionDto? condition)
    {
        if (condition is null) return "[NoDefectCondition Unconfigured]";
        var field = string.IsNullOrWhiteSpace(condition.Field) ? "Judge" : condition.Field.Trim();
        var op = condition.Operator?.Trim().ToLowerInvariant() ?? "equals";

        return op switch
        {
            "is_null" => $"[{field} IS NULL]",
            "is_not_null" => $"[{field} IS NOT NULL]",
            "is_empty" => $"[{field} IS EMPTY]",
            "is_not_empty" => $"[{field} IS NOT EMPTY]",
            "is_null_or_empty" => $"[{field} IS NULL OR EMPTY]",
            "in" => $"[{field} IN ({string.Join(", ", (condition.Values ?? []).Take(10).Select(v => $"'{v}'"))})]",
            "not_in" => $"[{field} NOT IN ({string.Join(", ", (condition.Values ?? []).Take(10).Select(v => $"'{v}'"))})]",
            "not_equals" => $"[{field} <> '{(condition.Value ?? condition.Values?.FirstOrDefault() ?? "")}']",
            _ => $"[{field} = '{(condition.Value ?? condition.Values?.FirstOrDefault() ?? "")}']"
        };
    }

    private static string FormatNameCondition(DefectBypassNameConditionDto? condition)
    {
        if (condition is null) return "[NameCondition Unconfigured]";
        var field = string.IsNullOrWhiteSpace(condition.Field) ? "DefectName" : condition.Field.Trim();
        var op = condition.Operator?.Trim().ToUpperInvariant() ?? "NOT IN";
        if (op == "NOT_IN") op = "NOT IN";
        if (op == "NOT_EQUALS") op = "NOT EQUALS";
        var values = (condition.Values ?? []).Take(10).Select(v => $"'{v}'");
        return $"[{field} {op} ({string.Join(", ", values)})]";
    }

    public static bool ShouldBypass(RawDefectRow row, DefectBypassRuleDto? rule)
    {
        if (rule is null || !rule.Enabled)
        {
            // Default behavior: bypass empty defect names and non-defects where Judge is 'G' or 'FD' or empty
            var name = row.DefectName?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Equals("None", StringComparison.OrdinalIgnoreCase) || name == "-" || name == "0" || name.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var j = row.Judge?.Trim();
            return string.IsNullOrWhiteSpace(j)
                || string.Equals(j, "G", StringComparison.OrdinalIgnoreCase)
                || string.Equals(j, "FD", StringComparison.OrdinalIgnoreCase);
        }

        return EvaluateRule(field => GetRawDefectRowField(row, field), rule);
    }

    public static bool ShouldBypass(
        IReadOnlyDictionary<string, object?> row,
        DefectTableMapping defectTable,
        DefectBypassRuleDto? rule)
    {
        if (rule is null || !rule.Enabled)
        {
            var nameVal = !string.IsNullOrWhiteSpace(defectTable.DefectNameColumn) && row.TryGetValue(defectTable.DefectNameColumn, out var nObj)
                ? nObj?.ToString()?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(nameVal) || nameVal.Equals("None", StringComparison.OrdinalIgnoreCase) || nameVal == "-" || nameVal == "0")
            {
                return true;
            }

            var judgeVal = !string.IsNullOrWhiteSpace(defectTable.JudgeColumn) && row.TryGetValue(defectTable.JudgeColumn, out var jObj)
                ? jObj?.ToString()?.Trim()
                : null;
            return string.IsNullOrWhiteSpace(judgeVal)
                || string.Equals(judgeVal, "G", StringComparison.OrdinalIgnoreCase)
                || string.Equals(judgeVal, "FD", StringComparison.OrdinalIgnoreCase);
        }

        return EvaluateRule(field => GetDictionaryField(row, defectTable, field), rule);
    }

    public static bool ShouldBypass(Func<string, string?> fieldGetter, DefectBypassRuleDto? rule)
    {
        if (rule is null || !rule.Enabled)
        {
            var name = fieldGetter("DefectName")?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Equals("None", StringComparison.OrdinalIgnoreCase) || name == "-" || name == "0")
            {
                return true;
            }

            var judgeVal = fieldGetter("Judge")?.Trim();
            return string.IsNullOrWhiteSpace(judgeVal)
                || string.Equals(judgeVal, "G", StringComparison.OrdinalIgnoreCase)
                || string.Equals(judgeVal, "FD", StringComparison.OrdinalIgnoreCase);
        }

        return EvaluateRule(fieldGetter, rule);
    }

    /// <summary>
    /// Builds a SQL WHERE predicate clause (e.g. " AND (`judge` IS NOT NULL AND UPPER(TRIM(`judge`)) NOT IN ('G', 'FD'))")
    /// to push down defect bypass filtering directly to the MariaDB engine, preventing transferring millions of 'G' rows over the network.
    /// </summary>
    public static string BuildSqlFilterClause(DefectTableMapping defectTable, DefectBypassRuleDto? rule)
    {
        // When rule is enabled, generate SQL predicate matching the configured rule
        if (rule is { Enabled: true })
        {
            var clauses = new List<string>();

            // 1. NoDefectCondition pushdown
            if (rule.NoDefectCondition is not null)
            {
                var targetCol = ResolveColumnName(defectTable, rule.NoDefectCondition.Field);
                if (!string.IsNullOrWhiteSpace(targetCol) && DefectBypassRuleConstants.IsSafeIdentifier(targetCol))
                {
                    var op = rule.NoDefectCondition.Operator?.Trim().ToLowerInvariant();
                    if (op is "equals" or "eq" or "==")
                    {
                        var rawVal = rule.NoDefectCondition.Value ?? rule.NoDefectCondition.Values?.FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(rawVal))
                        {
                            var values = rawVal.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            if (values.Length > 1)
                            {
                                var inList = string.Join(", ", values.Select(v => $"'{EscapeSql(v.ToUpperInvariant())}'"));
                                clauses.Add($"(`{targetCol}` IS NULL OR UPPER(TRIM(`{targetCol}`)) NOT IN ({inList}))");
                            }
                            else
                            {
                                clauses.Add($"(`{targetCol}` IS NULL OR UPPER(TRIM(`{targetCol}`)) <> '{EscapeSql(rawVal.ToUpperInvariant())}')");
                            }
                        }
                    }
                    else if (op is "in")
                    {
                        var values = (rule.NoDefectCondition.Values ?? [])
                            .Concat((rule.NoDefectCondition.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            .Where(v => !string.IsNullOrWhiteSpace(v))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Select(v => $"'{EscapeSql(v.Trim().ToUpperInvariant())}'")
                            .ToArray();
                        if (values.Length > 0)
                        {
                            clauses.Add($"(`{targetCol}` IS NULL OR UPPER(TRIM(`{targetCol}`)) NOT IN ({string.Join(", ", values)}))");
                        }
                    }
                    else if (op is "not_equals" or "neq" or "!=")
                    {
                        var rawVal = rule.NoDefectCondition.Value ?? rule.NoDefectCondition.Values?.FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(rawVal))
                        {
                            clauses.Add($"(`{targetCol}` IS NOT NULL AND UPPER(TRIM(`{targetCol}`)) = '{EscapeSql(rawVal.ToUpperInvariant())}')");
                        }
                    }
                }
            }

            // 2. NameCondition pushdown (if enabled and configured)
            if (rule.NameCondition is not null)
            {
                var nameCol = ResolveColumnName(defectTable, rule.NameCondition.Field ?? "DefectName");
                if (!string.IsNullOrWhiteSpace(nameCol) && DefectBypassRuleConstants.IsSafeIdentifier(nameCol))
                {
                    var op = rule.NameCondition.Operator?.Trim().ToUpperInvariant();
                    var values = (rule.NameCondition.Values ?? [])
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(v => $"'{EscapeSql(v.Trim().ToUpperInvariant())}'")
                        .ToArray();

                    if (values.Length > 0 && op is "NOT IN" or "NOT_IN")
                    {
                        clauses.Add($"(`{nameCol}` IS NULL OR UPPER(TRIM(`{nameCol}`)) NOT IN ({string.Join(", ", values)}))");
                    }
                    else if (values.Length > 0 && op is "EQUALS" or "IN")
                    {
                        clauses.Add($"(`{nameCol}` IS NULL OR UPPER(TRIM(`{nameCol}`)) NOT IN ({string.Join(", ", values)}))");
                    }
                }
            }

            if (clauses.Count > 0)
            {
                return " AND " + string.Join(" AND ", clauses);
            }
        }

        // Default fallback: exclude non-defects ('G', 'FD') and require valid non-empty defect name
        var fallbackClauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(defectTable.JudgeColumn) && DefectBypassRuleConstants.IsSafeIdentifier(defectTable.JudgeColumn))
        {
            fallbackClauses.Add($"(`{defectTable.JudgeColumn}` IS NOT NULL AND TRIM(`{defectTable.JudgeColumn}`) <> '' AND UPPER(TRIM(`{defectTable.JudgeColumn}`)) NOT IN ('G', 'FD'))");
        }
        if (!string.IsNullOrWhiteSpace(defectTable.DefectNameColumn) && DefectBypassRuleConstants.IsSafeIdentifier(defectTable.DefectNameColumn))
        {
            fallbackClauses.Add($"(`{defectTable.DefectNameColumn}` IS NOT NULL AND TRIM(`{defectTable.DefectNameColumn}`) <> '' AND UPPER(TRIM(`{defectTable.DefectNameColumn}`)) NOT IN ('NONE', '-', 'NULL', '0'))");
        }

        if (fallbackClauses.Count > 0)
        {
            return " AND " + string.Join(" AND ", fallbackClauses);
        }

        return string.Empty;
    }

    private static string? ResolveColumnName(DefectTableMapping defectTable, string? field)
    {
        if (string.IsNullOrWhiteSpace(field)) return defectTable.JudgeColumn;
        var norm = field.Trim();
        if (norm.Equals("Judge", StringComparison.OrdinalIgnoreCase)) return defectTable.JudgeColumn;
        if (norm.Equals("SpecJudge", StringComparison.OrdinalIgnoreCase)) return defectTable.SpecJudgeColumn ?? defectTable.JudgeColumn;
        if (norm.Equals("DefectName", StringComparison.OrdinalIgnoreCase) || norm.Equals("Name", StringComparison.OrdinalIgnoreCase)) return defectTable.DefectNameColumn;
        if (norm.Equals("DefectType", StringComparison.OrdinalIgnoreCase)) return defectTable.DefectTypeColumn;
        if (norm.Equals("Severity", StringComparison.OrdinalIgnoreCase)) return defectTable.SeverityColumn;
        if (norm.Equals("Reason", StringComparison.OrdinalIgnoreCase)) return defectTable.ReasonColumn;
        if (norm.Equals("SpecName", StringComparison.OrdinalIgnoreCase)) return defectTable.SpecNameColumn;
        if (norm.Equals("SpecReason", StringComparison.OrdinalIgnoreCase)) return defectTable.SpecReasonColumn;
        return defectTable.JudgeColumn;
    }

    private static string EscapeSql(string value) => value.Replace("'", "''").Replace("\\", "\\\\");

    private static bool EvaluateRule(Func<string, string?> fieldGetter, DefectBypassRuleDto rule)
    {
        var comparison = rule.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var nullPolicy = string.IsNullOrWhiteSpace(rule.NullPolicy) ? "PreserveRow" : rule.NullPolicy.Trim();

        // 1. Evaluate NoDefectCondition
        var noDefectMatch = false;
        if (rule.NoDefectCondition is not null)
        {
            var fieldVal = fieldGetter(rule.NoDefectCondition.Field);
            noDefectMatch = EvaluateCondition(
                rule.NoDefectCondition.Operator,
                fieldVal,
                rule.NoDefectCondition.Value,
                rule.NoDefectCondition.Values,
                comparison,
                nullPolicy,
                isNoDefectCondition: true);
        }

        // 2. Evaluate NameCondition
        var nameMatch = false;
        if (rule.NameCondition is not null)
        {
            var nameField = string.IsNullOrWhiteSpace(rule.NameCondition.Field) ? "DefectName" : rule.NameCondition.Field;
            var nameVal = fieldGetter(nameField);
            nameMatch = EvaluateCondition(
                rule.NameCondition.Operator,
                nameVal,
                null,
                rule.NameCondition.Values,
                comparison,
                nullPolicy,
                isNoDefectCondition: false);
        }

        // 3. Conjunction
        if (rule.NoDefectCondition is not null && rule.NameCondition is not null)
        {
            var isOr = string.Equals(rule.Conjunction?.Trim(), "OR", StringComparison.OrdinalIgnoreCase);
            return isOr ? (noDefectMatch || nameMatch) : (noDefectMatch && nameMatch);
        }

        if (rule.NoDefectCondition is not null) return noDefectMatch;
        if (rule.NameCondition is not null) return nameMatch;
        return false;
    }

    private static bool EvaluateCondition(
        string? op,
        string? rawValue,
        string? targetValue,
        IReadOnlyCollection<string>? targetValues,
        StringComparison comparison,
        string nullPolicy,
        bool isNoDefectCondition)
    {
        var normalizedOp = (op ?? "equals").Trim().ToLowerInvariant();

        if (normalizedOp is "is_null")
        {
            return rawValue is null;
        }
        if (normalizedOp is "is_not_null")
        {
            return rawValue is not null;
        }
        if (normalizedOp is "is_empty")
        {
            return rawValue is not null && rawValue.Length == 0;
        }
        if (normalizedOp is "is_not_empty")
        {
            return rawValue is not null && rawValue.Length > 0;
        }
        if (normalizedOp is "is_null_or_empty")
        {
            return string.IsNullOrEmpty(rawValue);
        }

        // Handle null raw value for value-based operators
        if (rawValue is null)
        {
            if (string.Equals(nullPolicy, "PreserveRow", StringComparison.OrdinalIgnoreCase))
            {
                // PreserveRow (default & safest): Null field NEVER satisfies any bypass condition; row is preserved
                return false;
            }

            if (string.Equals(nullPolicy, "MatchAsNoDefect", StringComparison.OrdinalIgnoreCase))
            {
                // MatchAsNoDefect:
                // For no-defect condition: null field is treated as matching no-defect (true)
                // For name condition: null name satisfies NOT IN / NOT EQUALS (true)
                if (isNoDefectCondition)
                {
                    return true;
                }
                if (normalizedOp is "not_in" or "not in" or "not_equals" or "not equals" or "!=" or "neq")
                {
                    return true;
                }
                return false;
            }

            if (string.Equals(nullPolicy, "Strict", StringComparison.OrdinalIgnoreCase))
            {
                // Strict 2-valued comparison: null is distinct from any non-null target literal,
                // so null satisfies NOT IN / NOT EQUALS, but does NOT satisfy EQUALS / IN
                if (normalizedOp is "not_in" or "not in" or "not_equals" or "not equals" or "!=" or "neq")
                {
                    return true;
                }
                return false;
            }

            return false;
        }

        var trimmed = rawValue.Trim();

        if (normalizedOp is "equals" or "eq" or "==")
        {
            var target = targetValue ?? targetValues?.FirstOrDefault() ?? string.Empty;
            return string.Equals(trimmed, target.Trim(), comparison);
        }

        if (normalizedOp is "not_equals" or "neq" or "!=")
        {
            var target = targetValue ?? targetValues?.FirstOrDefault() ?? string.Empty;
            return !string.Equals(trimmed, target.Trim(), comparison);
        }

        if (normalizedOp is "in")
        {
            var list = targetValues ?? (targetValue != null ? [targetValue] : Array.Empty<string>());
            return list.Any(item => string.Equals(trimmed, item.Trim(), comparison));
        }

        if (normalizedOp is "not in" or "not_in")
        {
            var list = targetValues ?? (targetValue != null ? [targetValue] : Array.Empty<string>());
            return !list.Any(item => string.Equals(trimmed, item.Trim(), comparison));
        }

        return false;
    }

    private static string? GetRawDefectRowField(RawDefectRow row, string field)
    {
        var normalized = field.Trim();
        if (normalized.Equals("Judge", StringComparison.OrdinalIgnoreCase)) return row.Judge;
        if (normalized.Equals("DefectName", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Name", StringComparison.OrdinalIgnoreCase)) return row.DefectName;
        if (normalized.Equals("SpecJudge", StringComparison.OrdinalIgnoreCase)) return row.SpecJudge;
        if (normalized.Equals("DefectType", StringComparison.OrdinalIgnoreCase)) return row.DefectType;
        if (normalized.Equals("Severity", StringComparison.OrdinalIgnoreCase)) return row.Severity;
        if (normalized.Equals("Reason", StringComparison.OrdinalIgnoreCase)) return row.Reason;
        if (normalized.Equals("SpecName", StringComparison.OrdinalIgnoreCase)) return row.SpecName;
        if (normalized.Equals("SpecReason", StringComparison.OrdinalIgnoreCase)) return row.SpecReason;
        return null;
    }

    private static string? GetDictionaryField(
        IReadOnlyDictionary<string, object?> row,
        DefectTableMapping defectTable,
        string field)
    {
        var normalized = field.Trim();
        string? colName = null;

        if (normalized.Equals("Judge", StringComparison.OrdinalIgnoreCase))
            colName = defectTable.JudgeColumn;
        else if (normalized.Equals("DefectName", StringComparison.OrdinalIgnoreCase) ||
                 normalized.Equals("Name", StringComparison.OrdinalIgnoreCase))
            colName = defectTable.DefectNameColumn;
        else if (normalized.Equals("SpecJudge", StringComparison.OrdinalIgnoreCase))
            colName = defectTable.SpecJudgeColumn;
        else if (normalized.Equals("DefectType", StringComparison.OrdinalIgnoreCase))
            colName = defectTable.DefectTypeColumn;
        else if (normalized.Equals("Severity", StringComparison.OrdinalIgnoreCase))
            colName = defectTable.SeverityColumn;
        else if (normalized.Equals("Reason", StringComparison.OrdinalIgnoreCase))
            colName = defectTable.ReasonColumn;
        else if (normalized.Equals("SpecName", StringComparison.OrdinalIgnoreCase))
            colName = defectTable.SpecNameColumn;
        else if (normalized.Equals("SpecReason", StringComparison.OrdinalIgnoreCase))
            colName = defectTable.SpecReasonColumn;

        if (!string.IsNullOrWhiteSpace(colName) && row.TryGetValue(colName, out var colVal))
        {
            return colVal?.ToString();
        }

        // Direct key fallback if matching by property name
        if (row.TryGetValue(normalized, out var directVal))
        {
            return directVal?.ToString();
        }

        var matchKey = row.Keys.FirstOrDefault(k => k.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (matchKey is not null)
        {
            return row[matchKey]?.ToString();
        }

        return null;
    }
}
