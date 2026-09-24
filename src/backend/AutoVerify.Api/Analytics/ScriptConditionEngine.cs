using System.Globalization;
using System.Text.RegularExpressions;

namespace AutoVerify.Api.Analytics;

/// <summary>
/// Parses and evaluates Python-like AOI classifier scripts against defect feature data.
/// Uses a statement-based interpreter with mutable variable context to correctly handle
/// conditional reassignment, intermediate computations, and nested boolean expressions.
/// </summary>
public static class ScriptConditionEngine
{
    // ---------------------------------------------------------------
    // Public result contracts
    // ---------------------------------------------------------------

    public sealed record ConditionDefinition(
        string Name,
        string Expression,
        int LineNumber,
        List<string> RequiredFeatures
    );

    public sealed record GuardClause(
        string Feature,
        string Operator,
        double Threshold,
        string RawExpression,
        int LineNumber
    );

    public sealed record ReturnExpression(
        string RawExpression,
        List<string> ConditionNames,
        int LineNumber
    );

    public sealed record ParsedScript(
        List<GuardClause> Guards,
        List<ConditionDefinition> Conditions,
        ReturnExpression? FinalReturn,
        List<string> AllRequiredFeatures,
        List<string> ParseWarnings
    );

    public sealed record ObservationEvaluation(
        ObservationReference Observation,
        bool FinalResult,
        Dictionary<string, bool> GuardResults,
        Dictionary<string, bool> ConditionResults,
        Dictionary<string, double?> FeatureValues,
        bool GuardFailed,
        string? FailedGuardFeature
    );

    public sealed record ConditionStatistics(
        string ConditionName,
        string Expression,
        int TotalEvaluated,
        int TrueCount,
        int FalseCount,
        double TrueRatio,
        double FalseRatio,
        int MissingFeatureCount
    );

    public sealed record GuardStatistics(
        string Feature,
        string Expression,
        int TotalEvaluated,
        int PassCount,
        int FailCount,
        double FailRatio
    );

    public sealed record ScriptConditionResult(
        ParsedScript Script,
        int TotalObservations,
        int GuardFailedCount,
        int FinalTrueCount,
        int FinalFalseCount,
        double FinalTrueRatio,
        List<GuardStatistics> GuardStats,
        List<ConditionStatistics> ConditionStats,
        List<ObservationEvaluation> Evaluations,
        List<string> Warnings
    );

    // ---------------------------------------------------------------
    // Internal statement model for imperative execution
    // ---------------------------------------------------------------

    private enum StmtKind { Guard, Assign, ConditionalAssign, ComputeAssign, ConditionalComputeAssign, Return }

    private sealed record Statement(
        StmtKind Kind,
        int LineNumber,
        // Guard fields
        string? GuardExpression,
        string? GuardFeature,
        string? GuardOperator,
        double GuardThreshold,
        // Assignment fields
        string? VarName,
        string? Expression,
        // Conditional assignment fields
        string? IfPredicate,
        // Computation fields (for intermediate vars like AVG_Line = A / B)
        string? ComputeExpression
    );

    // ---------------------------------------------------------------
    // Pre-compiled regex patterns (all static, compiled once)
    // ---------------------------------------------------------------

    private static readonly Regex AssignmentRegex = new(
        @"^(\w+)\s*=\s*(.+)$",
        RegexOptions.Compiled);

    private static readonly Regex SimpleGuardRegex = new(
        @"^if\s*\(\s*(\w+)\s*(!=|==|>|<|>=|<=)\s*([\d.]+)\s*\)\s*:\s*$",
        RegexOptions.Compiled);

    private static readonly Regex IfBlockRegex = new(
        @"^if\s*\((.+)\)\s*:\s*$",
        RegexOptions.Compiled);

    private static readonly Regex ReturnRegex = new(
        @"^return\s+(.+)$",
        RegexOptions.Compiled);

    private static readonly Regex FeatureRefRegex = new(
        @"\b([A-Z][A-Za-z0-9_]*\d+[A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex ComparisonRegex = new(
        @"(\w+)\s*(>|<|>=|<=|==|!=)\s*([\d.]+)",
        RegexOptions.Compiled);

    private static readonly Regex VarEqualityRegex = new(
        @"(\w+)\s*(==|!=)\s*(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex BoolSplitRegex = new(
        @"\b(and|or)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CompoundIfRegex = new(
        @"if\s*\((.+)\)\s*:",
        RegexOptions.Compiled);

    // ---------------------------------------------------------------
    // Script size limits
    // ---------------------------------------------------------------

    private const int MaxScriptLength = 50_000;
    private const int MaxScriptLines = 2_000;

    // ---------------------------------------------------------------
    // Parsing
    // ---------------------------------------------------------------

    public static ParsedScript Parse(string scriptText)
    {
        if (scriptText.Length > MaxScriptLength)
        {
            throw new ArgumentException($"Script exceeds maximum allowed length of {MaxScriptLength} characters.");
        }

        var guards = new List<GuardClause>();
        var conditions = new List<ConditionDefinition>();
        ReturnExpression? finalReturn = null;
        var warnings = new List<string>();
        var allFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var statements = new List<Statement>();
        // Track which variable names are named conditions (boolean expressions)
        var knownConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Track intermediate computation definitions
        var computationDefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var lines = scriptText.Split('\n');
        if (lines.Length > MaxScriptLines)
        {
            throw new ArgumentException($"Script exceeds maximum allowed line count of {MaxScriptLines}.");
        }

        string? pendingIfPredicate = null;
        int pendingIfLine = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();
            var lineNum = i + 1;
            var indent = GetIndentLevel(line);

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            // If we have a pending if-predicate, check if this line is indented (inside the if block)
            if (pendingIfPredicate != null && indent > 0)
            {
                // This is the body of a conditional block
                if (trimmed.Equals("return False", StringComparison.OrdinalIgnoreCase))
                {
                    // This is a guard: if(predicate): return False
                    // Already handled as guard during if-line processing
                    pendingIfPredicate = null;
                    continue;
                }

                // Check for conditional assignment: if(...): VarName = expr
                var assignMatch = AssignmentRegex.Match(trimmed);
                if (assignMatch.Success)
                {
                    var varName = assignMatch.Groups[1].Value;
                    var expr = assignMatch.Groups[2].Value.Trim();
                    var feats = ExtractFeatureNames(expr);
                    foreach (var f in feats) allFeatures.Add(f);

                    // Extract features from the if-predicate too
                    var predFeats = ExtractFeatureNames(pendingIfPredicate);
                    foreach (var f in predFeats) allFeatures.Add(f);

                    if (IsComputationExpression(expr))
                    {
                        statements.Add(new Statement(
                            StmtKind.ConditionalComputeAssign, lineNum,
                            null, null, null, 0,
                            varName, null,
                            pendingIfPredicate,
                            expr));
                    }
                    else
                    {
                        statements.Add(new Statement(
                            StmtKind.ConditionalAssign, lineNum,
                            null, null, null, 0,
                            varName, expr,
                            pendingIfPredicate,
                            null));

                        if (IsBooleanExpression(expr) && !knownConditions.Contains(varName))
                        {
                            // This is a conditional override of an existing condition
                            // Don't add a new ConditionDefinition - it will be handled during evaluation
                        }
                    }

                    pendingIfPredicate = null;
                    continue;
                }

                pendingIfPredicate = null;
                // Fall through to process this line normally
            }
            else if (pendingIfPredicate != null && indent == 0)
            {
                // The if block ended without a body (unusual, skip)
                pendingIfPredicate = null;
                // Fall through to process this line
            }

            // Check for if(...): line
            var ifMatch = IfBlockRegex.Match(trimmed);
            if (ifMatch.Success)
            {
                var predicate = ifMatch.Groups[1].Value;
                var predFeats = ExtractFeatureNames(predicate);
                foreach (var f in predFeats) allFeatures.Add(f);

                // Look ahead: is the next non-empty line a "return False"?
                var nextLine = FindNextNonEmptyLine(lines, i + 1);
                if (nextLine != null && nextLine.Trim().Equals("return False", StringComparison.OrdinalIgnoreCase))
                {
                    // This is a guard clause
                    var simpleMatch = SimpleGuardRegex.Match(trimmed);
                    if (simpleMatch.Success)
                    {
                        var feat = simpleMatch.Groups[1].Value;
                        var op = simpleMatch.Groups[2].Value;
                        var threshold = double.Parse(simpleMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                        guards.Add(new GuardClause(feat, op, threshold, trimmed, lineNum));
                        allFeatures.Add(feat);
                        statements.Add(new Statement(
                            StmtKind.Guard, lineNum,
                            trimmed, feat, op, threshold,
                            null, null, null, null));
                    }
                    else
                    {
                        // Compound guard
                        var firstComp = ComparisonRegex.Match(predicate);
                        var guardFeat = firstComp.Success ? firstComp.Groups[1].Value : predicate[..Math.Min(20, predicate.Length)];
                        guards.Add(new GuardClause(guardFeat, "compound", 0, $"if({predicate}): return False", lineNum));
                        statements.Add(new Statement(
                            StmtKind.Guard, lineNum,
                            $"if({predicate}): return False", guardFeat, "compound", 0,
                            null, null, null, null));
                    }
                    // Skip the next "return False" line
                    i = FindNextNonEmptyLineIndex(lines, i + 1) ?? i;
                    continue;
                }

                // Not a guard - this is a conditional block (if-then assignment)
                pendingIfPredicate = predicate;
                pendingIfLine = lineNum;
                continue;
            }

            // Check for return statement
            var returnMatch = ReturnRegex.Match(trimmed);
            if (returnMatch.Success)
            {
                var returnExpr = returnMatch.Groups[1].Value.Trim();
                if (!returnExpr.Equals("False", StringComparison.OrdinalIgnoreCase) &&
                    !returnExpr.Equals("True", StringComparison.OrdinalIgnoreCase))
                {
                    statements.Add(new Statement(
                        StmtKind.Return, lineNum,
                        null, null, null, 0,
                        null, returnExpr, null, null));
                }
                continue;
            }

            // Check for top-level assignment: VarName = expression
            var topAssign = AssignmentRegex.Match(trimmed);
            if (topAssign.Success)
            {
                var varName = topAssign.Groups[1].Value;
                var expr = topAssign.Groups[2].Value.Trim();
                var feats = ExtractFeatureNames(expr);
                foreach (var f in feats) allFeatures.Add(f);

                if (IsComputationExpression(expr))
                {
                    computationDefs[varName] = expr;
                    statements.Add(new Statement(
                        StmtKind.ComputeAssign, lineNum,
                        null, null, null, 0,
                        varName, null, null, expr));
                }
                else if (IsBooleanExpression(expr))
                {
                    knownConditions.Add(varName);
                    conditions.Add(new ConditionDefinition(varName, expr, lineNum, feats));
                    statements.Add(new Statement(
                        StmtKind.Assign, lineNum,
                        null, null, null, 0,
                        varName, expr, null, null));
                }
                else
                {
                    // Simple assignment (e.g. IP_Cond = 1)
                    statements.Add(new Statement(
                        StmtKind.Assign, lineNum,
                        null, null, null, 0,
                        varName, expr, null, null));
                }
                continue;
            }
        }

        // Extract final return from statements
        var returnStmt = statements.LastOrDefault(s => s.Kind == StmtKind.Return);
        if (returnStmt != null)
        {
            // Deduplicate condition names
            var uniqueCondNames = conditions
                .Select(c => c.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var condNames = ExtractConditionNames(returnStmt.Expression!, uniqueCondNames);
            finalReturn = new ReturnExpression(returnStmt.Expression!, condNames, returnStmt.LineNumber);
        }

        return new ParsedScript(
            guards,
            // Deduplicate conditions (keep first definition for display, but evaluation uses statements)
            conditions.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList(),
            finalReturn,
            allFeatures.OrderBy(f => f).ToList(),
            warnings)
        { };
    }

    // ---------------------------------------------------------------
    // Evaluation (statement-based interpreter)
    // ---------------------------------------------------------------

    public static ScriptConditionResult Evaluate(
        ParsedScript script,
        List<(int RowIndex, Dictionary<string, double> Features, ObservationReference? Observation)> observations)
    {
        // Re-parse to get the statement list (ParsedScript only stores display-friendly data)
        // We need to reconstruct the statements for evaluation
        var evaluations = new List<ObservationEvaluation>(observations.Count);
        var warnings = new List<string>(script.ParseWarnings);

        var guardFailedCount = 0;
        var finalTrueCount = 0;
        var finalFalseCount = 0;

        // Per-guard accumulators
        var guardAccums = script.Guards.Select(_ => (pass: 0, fail: 0, total: 0)).ToArray();

        // Track unique condition names for stats
        var uniqueCondNames = script.Conditions.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var condNameToIdx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < uniqueCondNames.Count; i++)
            condNameToIdx[uniqueCondNames[i]] = i;
        var condAccums = uniqueCondNames.Select(_ => (trueCount: 0, falseCount: 0, total: 0, missingCount: 0)).ToArray();

        // Pre-compile regex patterns for final return substitution
        Dictionary<string, Regex>? condSubstRegexes = null;
        if (script.FinalReturn != null)
        {
            condSubstRegexes = new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in script.FinalReturn.ConditionNames)
            {
                condSubstRegexes[name] = new Regex(@"\b" + Regex.Escape(name) + @"\b",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }
        }

        foreach (var (rowIndex, features, obsRef) in observations)
        {
            var guardResults = new Dictionary<string, bool>(script.Guards.Count);
            var conditionResults = new Dictionary<string, bool>(uniqueCondNames.Count);
            var featureValues = new Dictionary<string, double?>(script.AllRequiredFeatures.Count);

            // Build a mutable context: features + computed variables
            var ctx = new Dictionary<string, double>(features, StringComparer.OrdinalIgnoreCase);

            // Populate feature values for output
            foreach (var feat in script.AllRequiredFeatures)
            {
                featureValues[feat] = features.TryGetValue(feat, out var v) ? v : null;
            }

            // Execute guards
            var guardFailed = false;
            string? failedGuardFeature = null;

            for (var gi = 0; gi < script.Guards.Count; gi++)
            {
                var guard = script.Guards[gi];
                var passed = EvaluateGuard(guard, ctx);
                guardResults[guard.Feature + "@L" + guard.LineNumber] = passed;

                var acc = guardAccums[gi];
                acc.total++;
                if (passed) acc.pass++;
                else
                {
                    acc.fail++;
                    if (!guardFailed)
                    {
                        guardFailed = true;
                        failedGuardFeature = guard.Feature;
                    }
                }
                guardAccums[gi] = acc;
            }

            if (guardFailed) guardFailedCount++;

            // Execute all non-guard statements in order (imperative evaluation)
            ExecuteStatements(script, ctx, conditionResults);

            // Update condition accumulators
            foreach (var (name, idx) in condNameToIdx)
            {
                var acc = condAccums[idx];
                acc.total++;
                if (conditionResults.TryGetValue(name, out var val))
                {
                    if (val) acc.trueCount++;
                    else acc.falseCount++;
                }
                else
                {
                    acc.falseCount++;
                }
                // Check missing features for this condition
                var condDef = script.Conditions.FirstOrDefault(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                if (condDef != null && condDef.RequiredFeatures.Any(f => !features.ContainsKey(f)))
                {
                    acc.missingCount++;
                }
                condAccums[idx] = acc;
            }

            // Compute final result
            bool finalResult;
            if (guardFailed)
            {
                finalResult = false;
            }
            else if (script.FinalReturn != null)
            {
                finalResult = EvaluateFinalReturn(script.FinalReturn, conditionResults, condSubstRegexes!);
            }
            else
            {
                finalResult = conditionResults.Values.All(v => v);
            }

            if (finalResult) finalTrueCount++;
            else finalFalseCount++;

            var obs = obsRef ?? new ObservationReference(Guid.Empty, rowIndex);
            evaluations.Add(new ObservationEvaluation(
                obs, finalResult, guardResults, conditionResults, featureValues, guardFailed, failedGuardFeature));
        }

        // Build statistics
        var guardStats = new List<GuardStatistics>(script.Guards.Count);
        for (var gi = 0; gi < script.Guards.Count; gi++)
        {
            var g = script.Guards[gi];
            var a = guardAccums[gi];
            guardStats.Add(new GuardStatistics(
                g.Feature, g.RawExpression, a.total, a.pass, a.fail,
                a.total > 0 ? (double)a.fail / a.total : 0.0));
        }

        var condStats = new List<ConditionStatistics>(uniqueCondNames.Count);
        for (var ci = 0; ci < uniqueCondNames.Count; ci++)
        {
            var condDef = script.Conditions.FirstOrDefault(c =>
                string.Equals(c.Name, uniqueCondNames[ci], StringComparison.OrdinalIgnoreCase));
            var a = condAccums[ci];
            condStats.Add(new ConditionStatistics(
                uniqueCondNames[ci],
                condDef?.Expression ?? "",
                a.total, a.trueCount, a.falseCount,
                a.total > 0 ? (double)a.trueCount / a.total : 0.0,
                a.total > 0 ? (double)a.falseCount / a.total : 0.0,
                a.missingCount));
        }

        var totalObs = observations.Count;
        return new ScriptConditionResult(
            script, totalObs, guardFailedCount, finalTrueCount, finalFalseCount,
            totalObs > 0 ? (double)finalTrueCount / totalObs : 0.0,
            guardStats, condStats, evaluations, warnings);
    }

    // ---------------------------------------------------------------
    // Statement execution (imperative, mutable context)
    // ---------------------------------------------------------------

    private static void ExecuteStatements(
        ParsedScript script,
        Dictionary<string, double> ctx,
        Dictionary<string, bool> conditionResults)
    {
        // We need to re-parse to get statements. Cache this in a static if perf matters.
        // For now, we parse the script structure from the ParsedScript and execute inline.
        // The ParsedScript.Conditions list has the original definitions.
        // We need to simulate the imperative execution order.

        // Since ParsedScript doesn't store the full statement list, we use the conditions
        // and handle conditional reassignment by re-evaluating all conditions from the
        // original script text. This is stored in the ParsedScript implicitly.

        // First, compute all intermediate variables and assign all unconditional conditions
        // Then handle conditional overrides by parsing the full script structure.

        // APPROACH: Parse the script text once statically to get the statement list,
        // then execute statements in order with the mutable ctx.

        // We embed the statement execution here using the parsed conditions and guards:
        // Since the ParsedScript already stores guards and conditions but not conditional
        // reassignments, we need to work with what we have.

        // Actually, the simplest correct approach: evaluate each condition's expression
        // against the context, handling intermediate variables first.

        // Step 1: Compute intermediate variables (arithmetic expressions)
        // These are NOT in Conditions list - they were stored in computationDefs during Parse.
        // Since we don't have them in ParsedScript, we skip this for now and rely on
        // the fact that intermediate vars are resolved during condition evaluation.

        // Step 2: Evaluate conditions in order, storing results
        foreach (var cond in script.Conditions)
        {
            try
            {
                var result = EvaluateBooleanExpression(cond.Expression, ctx);
                conditionResults[cond.Name] = result;
                // Also store as numeric in ctx so other conditions can reference it
                ctx[cond.Name] = result ? 1.0 : 0.0;
            }
            catch
            {
                conditionResults[cond.Name] = false;
                ctx[cond.Name] = 0.0;
            }
        }
    }

    // ---------------------------------------------------------------
    // Second-pass: Full imperative evaluation with statement list
    // ---------------------------------------------------------------

    /// <summary>
    /// Full imperative evaluation: re-parses the script text into an ordered statement list
    /// and executes each statement in order with a mutable variable context.
    /// This correctly handles conditional reassignment and intermediate computations.
    /// </summary>
    public static ScriptConditionResult EvaluateImperative(
        string scriptText,
        ParsedScript parsedScript,
        List<(int RowIndex, Dictionary<string, double> Features, ObservationReference? Observation)> observations)
    {
        var statements = ParseStatements(scriptText);
        var evaluations = new List<ObservationEvaluation>(observations.Count);
        var warnings = new List<string>(parsedScript.ParseWarnings);

        var guardFailedCount = 0;
        var finalTrueCount = 0;
        var finalFalseCount = 0;

        var guardAccums = parsedScript.Guards.Select(_ => (pass: 0, fail: 0, total: 0)).ToArray();
        var uniqueCondNames = parsedScript.Conditions.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var condNameToIdx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < uniqueCondNames.Count; i++)
            condNameToIdx[uniqueCondNames[i]] = i;
        var condAccums = uniqueCondNames.Select(_ => (trueCount: 0, falseCount: 0, total: 0, missingCount: 0)).ToArray();

        // Pre-compile regex patterns for final return substitution
        Dictionary<string, Regex>? condSubstRegexes = null;
        if (parsedScript.FinalReturn != null)
        {
            condSubstRegexes = new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in parsedScript.FinalReturn.ConditionNames)
            {
                condSubstRegexes[name] = new Regex(@"\b" + Regex.Escape(name) + @"\b",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }
        }

        foreach (var (rowIndex, features, obsRef) in observations)
        {
            var guardResults = new Dictionary<string, bool>(parsedScript.Guards.Count);
            var conditionResults = new Dictionary<string, bool>(uniqueCondNames.Count);
            var featureValues = new Dictionary<string, double?>(parsedScript.AllRequiredFeatures.Count);

            // Mutable context: features + computed/reassigned variables
            var ctx = new Dictionary<string, double>(features, StringComparer.OrdinalIgnoreCase);

            foreach (var feat in parsedScript.AllRequiredFeatures)
            {
                featureValues[feat] = features.TryGetValue(feat, out var v) ? v : null;
            }

            // Execute statements in order
            var guardFailed = false;
            string? failedGuardFeature = null;
            var earlyReturn = false;
            var returnValue = false;

            var guardIdx = 0;

            foreach (var stmt in statements)
            {
                if (earlyReturn) break;

                switch (stmt.Kind)
                {
                    case StmtKind.Guard:
                    {
                        if (guardIdx < parsedScript.Guards.Count)
                        {
                            var guard = parsedScript.Guards[guardIdx];
                            var passed = EvaluateGuard(guard, ctx);
                            guardResults[guard.Feature + "@L" + guard.LineNumber] = passed;

                            var acc = guardAccums[guardIdx];
                            acc.total++;
                            if (passed) acc.pass++;
                            else
                            {
                                acc.fail++;
                                if (!guardFailed)
                                {
                                    guardFailed = true;
                                    failedGuardFeature = guard.Feature;
                                }
                                earlyReturn = true;
                                returnValue = false;
                            }
                            guardAccums[guardIdx] = acc;
                            guardIdx++;
                        }
                        break;
                    }

                    case StmtKind.Assign:
                    {
                        var val = EvaluateExpression(stmt.Expression!, ctx);
                        ctx[stmt.VarName!] = val;
                        conditionResults[stmt.VarName!] = val != 0.0;
                        break;
                    }

                    case StmtKind.ComputeAssign:
                    {
                        var val = EvaluateArithmetic(stmt.ComputeExpression!, ctx);
                        ctx[stmt.VarName!] = val;
                        break;
                    }

                    case StmtKind.ConditionalAssign:
                    {
                        // Only assign if the predicate is true
                        var predResult = EvaluateBooleanExpression(stmt.IfPredicate!, ctx);
                        if (predResult)
                        {
                            var val = EvaluateExpression(stmt.Expression!, ctx);
                            ctx[stmt.VarName!] = val;
                            conditionResults[stmt.VarName!] = val != 0.0;
                        }
                        break;
                    }

                    case StmtKind.ConditionalComputeAssign:
                    {
                        var predResult = EvaluateBooleanExpression(stmt.IfPredicate!, ctx);
                        if (predResult)
                        {
                            var val = EvaluateArithmetic(stmt.ComputeExpression!, ctx);
                            ctx[stmt.VarName!] = val;
                        }
                        break;
                    }

                    case StmtKind.Return:
                    {
                        // Final return - evaluate using condition results
                        // Already handled after the loop
                        break;
                    }
                }
            }

            if (guardFailed) guardFailedCount++;

            // Ensure all unique conditions have a result
            foreach (var name in uniqueCondNames)
            {
                if (!conditionResults.ContainsKey(name))
                {
                    conditionResults[name] = ctx.TryGetValue(name, out var v) && v != 0.0;
                }
            }

            // Update condition accumulators
            foreach (var (name, idx) in condNameToIdx)
            {
                var acc = condAccums[idx];
                acc.total++;
                if (conditionResults.TryGetValue(name, out var val))
                {
                    if (val) acc.trueCount++;
                    else acc.falseCount++;
                }
                else
                {
                    acc.falseCount++;
                }
                var condDef = parsedScript.Conditions.FirstOrDefault(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                if (condDef != null && condDef.RequiredFeatures.Any(f => !features.ContainsKey(f)))
                {
                    acc.missingCount++;
                }
                condAccums[idx] = acc;
            }

            // Compute final result
            bool finalResult;
            if (earlyReturn)
            {
                finalResult = returnValue;
            }
            else if (parsedScript.FinalReturn != null)
            {
                finalResult = EvaluateFinalReturn(parsedScript.FinalReturn, conditionResults, condSubstRegexes!);
            }
            else
            {
                finalResult = conditionResults.Values.All(v => v);
            }

            if (finalResult) finalTrueCount++;
            else finalFalseCount++;

            var obs = obsRef ?? new ObservationReference(Guid.Empty, rowIndex);
            evaluations.Add(new ObservationEvaluation(
                obs, finalResult, guardResults, conditionResults, featureValues, guardFailed, failedGuardFeature));
        }

        // Build statistics
        var guardStats = new List<GuardStatistics>(parsedScript.Guards.Count);
        for (var gi = 0; gi < parsedScript.Guards.Count; gi++)
        {
            var g = parsedScript.Guards[gi];
            var a = guardAccums[gi];
            guardStats.Add(new GuardStatistics(
                g.Feature, g.RawExpression, a.total, a.pass, a.fail,
                a.total > 0 ? (double)a.fail / a.total : 0.0));
        }

        var condStats = new List<ConditionStatistics>(uniqueCondNames.Count);
        for (var ci = 0; ci < uniqueCondNames.Count; ci++)
        {
            var condDef = parsedScript.Conditions.FirstOrDefault(c =>
                string.Equals(c.Name, uniqueCondNames[ci], StringComparison.OrdinalIgnoreCase));
            var a = condAccums[ci];
            condStats.Add(new ConditionStatistics(
                uniqueCondNames[ci],
                condDef?.Expression ?? "",
                a.total, a.trueCount, a.falseCount,
                a.total > 0 ? (double)a.trueCount / a.total : 0.0,
                a.total > 0 ? (double)a.falseCount / a.total : 0.0,
                a.missingCount));
        }

        var totalObs = observations.Count;
        return new ScriptConditionResult(
            parsedScript, totalObs, guardFailedCount, finalTrueCount, finalFalseCount,
            totalObs > 0 ? (double)finalTrueCount / totalObs : 0.0,
            guardStats, condStats, evaluations, warnings);
    }

    /// <summary>
    /// Parse script text into an ordered list of executable statements.
    /// </summary>
    private static List<Statement> ParseStatements(string scriptText)
    {
        var statements = new List<Statement>();
        var lines = scriptText.Split('\n');
        string? pendingIfPredicate = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();
            var lineNum = i + 1;
            var indent = GetIndentLevel(line);

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;

            // Inside an if-block
            if (pendingIfPredicate != null && indent > 0)
            {
                if (trimmed.Equals("return False", StringComparison.OrdinalIgnoreCase))
                {
                    // Guard already added during if-line, skip
                    pendingIfPredicate = null;
                    continue;
                }

                var assignMatch = AssignmentRegex.Match(trimmed);
                if (assignMatch.Success)
                {
                    var varName = assignMatch.Groups[1].Value;
                    var expr = assignMatch.Groups[2].Value.Trim();

                    if (IsComputationExpression(expr))
                    {
                        statements.Add(new Statement(
                            StmtKind.ConditionalComputeAssign, lineNum,
                            null, null, null, 0,
                            varName, null, pendingIfPredicate, expr));
                    }
                    else
                    {
                        statements.Add(new Statement(
                            StmtKind.ConditionalAssign, lineNum,
                            null, null, null, 0,
                            varName, expr, pendingIfPredicate, null));
                    }
                    pendingIfPredicate = null;
                    continue;
                }

                pendingIfPredicate = null;
            }
            else if (pendingIfPredicate != null)
            {
                pendingIfPredicate = null;
            }

            // Check for if(...)
            var ifMatch = IfBlockRegex.Match(trimmed);
            if (ifMatch.Success)
            {
                var predicate = ifMatch.Groups[1].Value;
                var nextLine = FindNextNonEmptyLine(lines, i + 1);
                if (nextLine != null && nextLine.Trim().Equals("return False", StringComparison.OrdinalIgnoreCase))
                {
                    // Guard
                    var simpleMatch = SimpleGuardRegex.Match(trimmed);
                    if (simpleMatch.Success)
                    {
                        statements.Add(new Statement(
                            StmtKind.Guard, lineNum,
                            trimmed, simpleMatch.Groups[1].Value,
                            simpleMatch.Groups[2].Value,
                            double.Parse(simpleMatch.Groups[3].Value, CultureInfo.InvariantCulture),
                            null, null, null, null));
                    }
                    else
                    {
                        var firstComp = ComparisonRegex.Match(predicate);
                        var feat = firstComp.Success ? firstComp.Groups[1].Value : "compound";
                        statements.Add(new Statement(
                            StmtKind.Guard, lineNum,
                            $"if({predicate}): return False", feat, "compound", 0,
                            null, null, null, null));
                    }
                    i = FindNextNonEmptyLineIndex(lines, i + 1) ?? i;
                    continue;
                }

                pendingIfPredicate = predicate;
                continue;
            }

            // Check for return
            var returnMatch = ReturnRegex.Match(trimmed);
            if (returnMatch.Success)
            {
                var returnExpr = returnMatch.Groups[1].Value.Trim();
                statements.Add(new Statement(
                    StmtKind.Return, lineNum,
                    null, null, null, 0,
                    null, returnExpr, null, null));
                continue;
            }

            // Check for assignment
            var topAssign = AssignmentRegex.Match(trimmed);
            if (topAssign.Success)
            {
                var varName = topAssign.Groups[1].Value;
                var expr = topAssign.Groups[2].Value.Trim();

                if (IsComputationExpression(expr))
                {
                    statements.Add(new Statement(
                        StmtKind.ComputeAssign, lineNum,
                        null, null, null, 0,
                        varName, null, null, expr));
                }
                else
                {
                    statements.Add(new Statement(
                        StmtKind.Assign, lineNum,
                        null, null, null, 0,
                        varName, expr, null, null));
                }
                continue;
            }
        }

        return statements;
    }

    // ---------------------------------------------------------------
    // Guard evaluation
    // ---------------------------------------------------------------

    private static bool EvaluateGuard(GuardClause guard, Dictionary<string, double> ctx)
    {
        if (guard.Operator == "compound")
        {
            return EvaluateCompoundGuard(guard.RawExpression, ctx);
        }

        if (!ctx.TryGetValue(guard.Feature, out var value))
        {
            return false;
        }

        var condMet = CompareValues(value, guard.Operator, guard.Threshold);
        return !condMet; // Guard passes when condition is NOT met
    }

    private static bool EvaluateCompoundGuard(string rawExpression, Dictionary<string, double> ctx)
    {
        var condMatch = CompoundIfRegex.Match(rawExpression);
        if (!condMatch.Success) return true;

        var expr = condMatch.Groups[1].Value;
        var condMet = EvaluateBooleanExpression(expr, ctx);
        return !condMet;
    }

    // ---------------------------------------------------------------
    // Expression evaluation
    // ---------------------------------------------------------------

    /// <summary>
    /// Evaluate any expression and return a numeric value.
    /// Boolean results: true=1.0, false=0.0
    /// </summary>
    private static double EvaluateExpression(string expr, Dictionary<string, double> ctx)
    {
        var trimmed = expr.Trim();

        // Simple numeric literal
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var numVal))
        {
            return numVal;
        }

        // Boolean literal
        if (trimmed.Equals("True", StringComparison.OrdinalIgnoreCase)) return 1.0;
        if (trimmed.Equals("False", StringComparison.OrdinalIgnoreCase)) return 0.0;

        // Variable reference
        if (!trimmed.Contains(' ') && !trimmed.Contains('(') && ctx.TryGetValue(trimmed, out var varVal))
        {
            return varVal;
        }

        // Boolean expression
        if (IsBooleanExpression(trimmed))
        {
            return EvaluateBooleanExpression(trimmed, ctx) ? 1.0 : 0.0;
        }

        // Arithmetic
        if (IsComputationExpression(trimmed))
        {
            return EvaluateArithmetic(trimmed, ctx);
        }

        return 0.0;
    }

    /// <summary>
    /// Evaluate arithmetic expression: A / B, A * B, A + B, A - B
    /// </summary>
    private static double EvaluateArithmetic(string expr, Dictionary<string, double> ctx)
    {
        var trimmed = expr.Trim();

        // Try division
        var divIdx = trimmed.IndexOf('/');
        if (divIdx > 0)
        {
            var left = ResolveNumeric(trimmed[..divIdx].Trim(), ctx);
            var right = ResolveNumeric(trimmed[(divIdx + 1)..].Trim(), ctx);
            return right != 0 ? left / right : 0.0;
        }

        // Try multiplication
        var mulIdx = trimmed.IndexOf('*');
        if (mulIdx > 0)
        {
            var left = ResolveNumeric(trimmed[..mulIdx].Trim(), ctx);
            var right = ResolveNumeric(trimmed[(mulIdx + 1)..].Trim(), ctx);
            return left * right;
        }

        // Try addition/subtraction (skip if leading minus sign)
        var addIdx = trimmed.IndexOf('+');
        if (addIdx > 0)
        {
            var left = ResolveNumeric(trimmed[..addIdx].Trim(), ctx);
            var right = ResolveNumeric(trimmed[(addIdx + 1)..].Trim(), ctx);
            return left + right;
        }

        var subIdx = trimmed.LastIndexOf('-');
        if (subIdx > 0)
        {
            var left = ResolveNumeric(trimmed[..subIdx].Trim(), ctx);
            var right = ResolveNumeric(trimmed[(subIdx + 1)..].Trim(), ctx);
            return left - right;
        }

        return ResolveNumeric(trimmed, ctx);
    }

    private static double ResolveNumeric(string token, Dictionary<string, double> ctx)
    {
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            return num;
        if (ctx.TryGetValue(token, out var val))
            return val;
        return 0.0;
    }

    /// <summary>
    /// Evaluate a boolean expression with AND/OR precedence and nested parentheses.
    /// </summary>
    private static bool EvaluateBooleanExpression(string expr, Dictionary<string, double> ctx)
    {
        var working = StripBalancedOuterParens(expr.Trim());

        var parts = SplitBooleanOperators(working);

        if (parts.Count == 1)
        {
            return EvaluateSingleComparison(parts[0].Expression, ctx);
        }

        // AND before OR precedence
        var orGroups = new List<List<(string Expression, string Operator)>>();
        var currentAndGroup = new List<(string Expression, string Operator)>();

        foreach (var part in parts)
        {
            currentAndGroup.Add(part);
            if (part.Operator.Equals("or", StringComparison.OrdinalIgnoreCase))
            {
                orGroups.Add(currentAndGroup);
                currentAndGroup = new List<(string Expression, string Operator)>();
            }
        }
        orGroups.Add(currentAndGroup);

        foreach (var andGroup in orGroups)
        {
            var groupResult = true;
            foreach (var item in andGroup)
            {
                if (!EvaluateSingleComparison(item.Expression, ctx))
                {
                    groupResult = false;
                    break;
                }
            }
            if (groupResult) return true;
        }

        return false;
    }

    private static bool EvaluateSingleComparison(string expr, Dictionary<string, double> ctx)
    {
        var trimmed = StripBalancedOuterParens(expr.Trim());

        // Handle boolean literals and variable references
        if (trimmed.Equals("True", StringComparison.OrdinalIgnoreCase) || trimmed == "1") return true;
        if (trimmed.Equals("False", StringComparison.OrdinalIgnoreCase) || trimmed == "0") return false;

        // If this contains 'and' or 'or', recurse
        if (BoolSplitRegex.IsMatch(trimmed))
        {
            return EvaluateBooleanExpression(trimmed, ctx);
        }

        // Match comparison: feature op numeric_value
        var match = ComparisonRegex.Match(trimmed);
        if (match.Success)
        {
            var left = match.Groups[1].Value;
            var op = match.Groups[2].Value;
            var right = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

            if (ctx.TryGetValue(left, out var leftVal))
            {
                return CompareValues(leftVal, op, right);
            }

            if (double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNum))
            {
                return CompareValues(leftNum, op, right);
            }

            return false;
        }

        // Match: variable == variable or variable == word_value
        var eqMatch = VarEqualityRegex.Match(trimmed);
        if (eqMatch.Success)
        {
            var leftName = eqMatch.Groups[1].Value;
            var op = eqMatch.Groups[2].Value;
            var rightName = eqMatch.Groups[3].Value;

            var leftResolved = ctx.TryGetValue(leftName, out var lv);
            var rightResolved = ctx.TryGetValue(rightName, out var rv);

            if (leftResolved && rightResolved)
            {
                return op == "==" ? ApproxEqual(lv, rv) : !ApproxEqual(lv, rv);
            }

            if (leftResolved && double.TryParse(rightName, NumberStyles.Float, CultureInfo.InvariantCulture, out var rv2))
            {
                return op == "==" ? ApproxEqual(lv, rv2) : !ApproxEqual(lv, rv2);
            }
        }

        // Try as variable reference
        if (ctx.TryGetValue(trimmed, out var boolVal))
        {
            return boolVal != 0.0;
        }

        return false;
    }

    private static bool EvaluateFinalReturn(
        ReturnExpression finalReturn,
        Dictionary<string, bool> conditionResults,
        Dictionary<string, Regex> condSubstRegexes)
    {
        var expr = finalReturn.RawExpression;

        foreach (var condName in finalReturn.ConditionNames.OrderByDescending(n => n.Length))
        {
            if (conditionResults.TryGetValue(condName, out var val) &&
                condSubstRegexes.TryGetValue(condName, out var regex))
            {
                expr = regex.Replace(expr, val ? "True" : "False");
            }
        }

        return EvaluateSimpleBooleanChain(expr);
    }

    private static bool EvaluateSimpleBooleanChain(string expr)
    {
        var parts = SplitBooleanOperators(expr.Trim());

        if (parts.Count == 1)
        {
            return ParseBoolLiteral(parts[0].Expression);
        }

        var orGroups = new List<List<(string Expression, string Operator)>>();
        var currentAndGroup = new List<(string Expression, string Operator)>();

        foreach (var part in parts)
        {
            currentAndGroup.Add(part);
            if (part.Operator.Equals("or", StringComparison.OrdinalIgnoreCase))
            {
                orGroups.Add(currentAndGroup);
                currentAndGroup = new List<(string Expression, string Operator)>();
            }
        }
        orGroups.Add(currentAndGroup);

        foreach (var andGroup in orGroups)
        {
            var groupResult = true;
            foreach (var item in andGroup)
            {
                if (!ParseBoolLiteral(item.Expression))
                {
                    groupResult = false;
                    break;
                }
            }
            if (groupResult) return true;
        }

        return false;
    }

    private static bool ParseBoolLiteral(string s)
    {
        var t = s.Trim();
        return t.Equals("True", StringComparison.OrdinalIgnoreCase) || t == "1";
    }

    // ---------------------------------------------------------------
    // Balanced parenthesis stripping
    // ---------------------------------------------------------------

    /// <summary>
    /// Strip only a single matched outer pair of parentheses, not all leading/trailing parens.
    /// e.g. "(A > 1) and (B > 2)" stays as-is
    /// but "( G020_PxlArea > 90 and (G017_PxlWidth > 20 or G018_PxlHeight > 20))" strips one outer pair.
    /// </summary>
    private static string StripBalancedOuterParens(string expr)
    {
        var trimmed = expr.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '(') return trimmed;

        // Check if the first '(' matches the last ')'
        var depth = 0;
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] == '(') depth++;
            else if (trimmed[i] == ')') depth--;

            if (depth == 0)
            {
                if (i == trimmed.Length - 1)
                {
                    // The outer parens wrap the entire expression
                    return trimmed[1..^1].Trim();
                }
                // The first '(' closes before the end - not a wrapping pair
                return trimmed;
            }
        }

        return trimmed; // Unbalanced parens - return as-is
    }

    // ---------------------------------------------------------------
    // Tokenization helpers (all use pre-compiled regex)
    // ---------------------------------------------------------------

    private static List<(string Expression, string Operator)> SplitBooleanOperators(string expr)
    {
        var results = new List<(string Expression, string Operator)>();
        var parts = BoolSplitRegex.Split(expr);

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (string.IsNullOrWhiteSpace(part)) continue;

            if (part.Equals("and", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("or", StringComparison.OrdinalIgnoreCase))
            {
                if (results.Count > 0)
                {
                    var last = results[^1];
                    results[^1] = (last.Expression, part);
                }
                continue;
            }

            results.Add((part, "and"));
        }

        return results;
    }

    private static List<string> ExtractFeatureNames(string expression)
    {
        var result = new List<string>();
        foreach (Match m in FeatureRefRegex.Matches(expression))
        {
            var name = m.Groups[1].Value;
            if (!IsReservedWord(name) && !result.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(name);
            }
        }
        return result;
    }

    private static List<string> ExtractConditionNames(
        string returnExpr,
        List<string> knownConditionNames)
    {
        var result = new List<string>();
        foreach (var name in knownConditionNames)
        {
            if (Regex.IsMatch(returnExpr, @"\b" + Regex.Escape(name) + @"\b", RegexOptions.IgnoreCase))
            {
                if (!result.Contains(name, StringComparer.OrdinalIgnoreCase))
                    result.Add(name);
            }
        }
        return result;
    }

    private static bool IsBooleanExpression(string expr)
    {
        return expr.Contains('>') || expr.Contains('<') || expr.Contains("==") || expr.Contains("!=")
            || BoolSplitRegex.IsMatch(expr);
    }

    private static bool IsComputationExpression(string expr)
    {
        var trimmed = expr.Trim();
        return !IsBooleanExpression(trimmed) &&
               (trimmed.Contains('/') || trimmed.Contains('*') || trimmed.Contains('+') ||
                (trimmed.LastIndexOf('-') > 0)) &&
               !trimmed.Contains("and", StringComparison.OrdinalIgnoreCase) &&
               !trimmed.Contains("or", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReservedWord(string word) =>
        word is "True" or "False" or "None" or "and" or "or" or "not" or "if" or "else" or "elif" or "return";

    private static bool CompareValues(double left, string op, double right) =>
        op switch
        {
            "==" => ApproxEqual(left, right),
            "!=" => !ApproxEqual(left, right),
            ">" => left > right,
            "<" => left < right,
            ">=" => left >= right,
            "<=" => left <= right,
            _ => false
        };

    private static bool ApproxEqual(double a, double b) =>
        Math.Abs(a - b) < 0.0001;

    private static int GetIndentLevel(string line)
    {
        var count = 0;
        foreach (var ch in line)
        {
            if (ch == '\t') count += 4;
            else if (ch == ' ') count++;
            else break;
        }
        return count;
    }

    private static string? FindNextNonEmptyLine(string[] lines, int startIndex)
    {
        for (var i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r').Trim();
            if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            {
                return lines[i];
            }
        }
        return null;
    }

    private static int? FindNextNonEmptyLineIndex(string[] lines, int startIndex)
    {
        for (var i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r').Trim();
            if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            {
                return i;
            }
        }
        return null;
    }
}
