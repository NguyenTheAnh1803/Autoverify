namespace AutoVerify.Api.Analytics;

public sealed record NelsonRuleViolation(
    int Index,
    int RuleId,
    string RuleName,
    string Description,
    string Severity = "Warning"
);

/// <summary>
/// Evaluates Nelson Rules (Rules 1 through 8) for Shewhart and Individual control charts.
/// </summary>
public static class NelsonRulesEngine
{
    public static List<NelsonRuleViolation> Evaluate(IReadOnlyList<double> values, double centerLine, double sigma)
    {
        var violations = new List<NelsonRuleViolation>();
        if (values == null || values.Count == 0 || sigma <= 0 || double.IsNaN(sigma) || double.IsInfinity(sigma))
        {
            return violations;
        }

        var n = values.Count;

        for (var i = 0; i < n; i++)
        {
            var val = values[i];
            var diff = val - centerLine;
            var absDiff = Math.Abs(diff);

            // Rule 1: One point beyond 3 sigma (Zone A or beyond)
            if (absDiff > 3.0 * sigma)
            {
                violations.Add(new NelsonRuleViolation(
                    i, 1, "Rule 1: Beyond 3 Sigma",
                    $"Point ({val:G5}) is beyond 3 sigma limit ({centerLine + (diff > 0 ? 3 : -3) * sigma:G5})",
                    "Critical"));
            }

            // Rule 2: Nine (or more) points in a row on the same side of the center line
            if (i >= 8)
            {
                var sign = Math.Sign(values[i] - centerLine);
                if (sign != 0)
                {
                    var allSameSide = true;
                    for (var j = i - 8; j <= i; j++)
                    {
                        if (Math.Sign(values[j] - centerLine) != sign)
                        {
                            allSameSide = false;
                            break;
                        }
                    }

                    if (allSameSide)
                    {
                        violations.Add(new NelsonRuleViolation(
                            i, 2, "Rule 2: Nine Points on One Side",
                            "9 consecutive points on the same side of the center line"));
                    }
                }
            }

            // Rule 3: Six (or more) points in a row continually increasing or continually decreasing
            if (i >= 5)
            {
                var inc = true;
                var dec = true;
                for (var j = i - 4; j <= i; j++)
                {
                    if (values[j] <= values[j - 1]) inc = false;
                    if (values[j] >= values[j - 1]) dec = false;
                }

                if (inc || dec)
                {
                    violations.Add(new NelsonRuleViolation(
                        i, 3, "Rule 3: Six Points Trend",
                        inc ? "6 consecutive points strictly increasing" : "6 consecutive points strictly decreasing"));
                }
            }

            // Rule 4: Fourteen points in a row alternating up and down
            if (i >= 13)
            {
                var alternating = true;
                for (var j = i - 11; j <= i; j++)
                {
                    var d1 = values[j] - values[j - 1];
                    var d0 = values[j - 1] - values[j - 2];
                    if (d1 * d0 >= 0)
                    {
                        alternating = false;
                        break;
                    }
                }

                if (alternating)
                {
                    violations.Add(new NelsonRuleViolation(
                        i, 4, "Rule 4: Fourteen Points Alternating",
                        "14 consecutive points alternating direction"));
                }
            }

            // Rule 5: Two out of three points in a row in Zone A or beyond on the same side of center line (>2 sigma)
            if (i >= 2)
            {
                CheckRule5(i, values, centerLine, sigma, violations);
            }

            // Rule 6: Four out of five points in a row in Zone B or beyond on the same side of center line (>1 sigma)
            if (i >= 4)
            {
                CheckRule6(i, values, centerLine, sigma, violations);
            }

            // Rule 7: Fifteen points in a row in Zone C (within 1 sigma of center line on either side)
            if (i >= 14)
            {
                var allInZoneC = true;
                for (var j = i - 14; j <= i; j++)
                {
                    if (Math.Abs(values[j] - centerLine) >= sigma)
                    {
                        allInZoneC = false;
                        break;
                    }
                }

                if (allInZoneC)
                {
                    violations.Add(new NelsonRuleViolation(
                        i, 7, "Rule 7: Fifteen Points in Zone C",
                        "15 consecutive points within 1 sigma (stratification/reduced variability)"));
                }
            }

            // Rule 8: Eight points in a row with none in Zone C (> 1 sigma from center line on either side)
            if (i >= 7)
            {
                var noneInZoneC = true;
                for (var j = i - 7; j <= i; j++)
                {
                    if (Math.Abs(values[j] - centerLine) <= sigma)
                    {
                        noneInZoneC = false;
                        break;
                    }
                }

                if (noneInZoneC)
                {
                    violations.Add(new NelsonRuleViolation(
                        i, 8, "Rule 8: Eight Points Outside Zone C",
                        "8 consecutive points outside 1 sigma on either side (mixture pattern)"));
                }
            }
        }

        return violations;
    }

    private static void CheckRule5(int i, IReadOnlyList<double> values, double centerLine, double sigma, List<NelsonRuleViolation> violations)
    {
        // Check upper side > +2 sigma
        var countUpper = 0;
        for (var j = i - 2; j <= i; j++)
        {
            if (values[j] - centerLine > 2.0 * sigma) countUpper++;
        }
        if (countUpper >= 2 && values[i] - centerLine > 2.0 * sigma)
        {
            violations.Add(new NelsonRuleViolation(
                i, 5, "Rule 5: Two of Three > 2 Sigma",
                "2 out of 3 consecutive points in upper Zone A or beyond"));
            return;
        }

        // Check lower side < -2 sigma
        var countLower = 0;
        for (var j = i - 2; j <= i; j++)
        {
            if (values[j] - centerLine < -2.0 * sigma) countLower++;
        }
        if (countLower >= 2 && values[i] - centerLine < -2.0 * sigma)
        {
            violations.Add(new NelsonRuleViolation(
                i, 5, "Rule 5: Two of Three > 2 Sigma",
                "2 out of 3 consecutive points in lower Zone A or beyond"));
        }
    }

    private static void CheckRule6(int i, IReadOnlyList<double> values, double centerLine, double sigma, List<NelsonRuleViolation> violations)
    {
        // Check upper side > +1 sigma
        var countUpper = 0;
        for (var j = i - 4; j <= i; j++)
        {
            if (values[j] - centerLine > sigma) countUpper++;
        }
        if (countUpper >= 4 && values[i] - centerLine > sigma)
        {
            violations.Add(new NelsonRuleViolation(
                i, 6, "Rule 6: Four of Five > 1 Sigma",
                "4 out of 5 consecutive points in upper Zone B or beyond"));
            return;
        }

        // Check lower side < -1 sigma
        var countLower = 0;
        for (var j = i - 4; j <= i; j++)
        {
            if (values[j] - centerLine < -sigma) countLower++;
        }
        if (countLower >= 4 && values[i] - centerLine < -sigma)
        {
            violations.Add(new NelsonRuleViolation(
                i, 6, "Rule 6: Four of Five > 1 Sigma",
                "4 out of 5 consecutive points in lower Zone B or beyond"));
        }
    }
}
