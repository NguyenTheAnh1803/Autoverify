using MathNet.Numerics.Distributions;

namespace AutoVerify.Api.Analytics;

public sealed record MsaAnovaRow(
    string Source,
    int DegreesOfFreedom,
    double SumOfSquares,
    double MeanSquare,
    double? FStatistic,
    double? PValue
);

public sealed record MsaVarianceComponent(
    string Source,
    double Variance,
    double StandardDeviation,
    double StudyVariation, // 6 * sigma
    double PercentContribution, // % of total variance
    double PercentStudyVar, // % of total std dev
    double? PercentTolerance // % of tolerance (if provided)
);

public sealed record MsaResult(
    string FeatureName,
    int PartCount,
    int OperatorCount,
    int RepeatCount,
    int TotalMeasurements,
    bool IsBalanced,
    bool InteractionSignificant,
    bool WasInteractionPooled,
    int NumberOfDistinctCategories,
    string StatusAssessment,
    List<MsaAnovaRow> AnovaTable,
    List<MsaVarianceComponent> VarianceComponents,
    List<string> Blockers,
    List<string> Warnings
);

public static class MsaEngine
{
    public static MsaResult Compute(
        IReadOnlyList<(string PartId, string OperatorId, double Value)> data,
        string featureName,
        double? tolerance = null,
        double alphaInteraction = 0.05)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();

        if (data == null || data.Count == 0)
        {
            blockers.Add("No measurement data provided for Gage R&R.");
            return CreateEmptyResult(featureName, blockers, warnings);
        }

        var partGroups = data.GroupBy(x => x.PartId).ToList();
        var operGroups = data.GroupBy(x => x.OperatorId).ToList();

        var a = partGroups.Count; // parts
        var b = operGroups.Count; // operators

        if (a < 2) blockers.Add($"At least 2 unique parts are required (found {a}). Industry recommendation is >= 10 parts.");
        if (b < 2) blockers.Add($"At least 2 unique operators are required (found {b}).");

        if (a < 10)
        {
            warnings.Add($"Sample has only {a} parts. AIAG MSA 4th edition recommends at least 10 parts for reliable Gage R&R.");
        }

        // Check cell replicates (part x operator)
        var cells = data.GroupBy(x => (x.PartId, x.OperatorId)).ToList();
        var cellCounts = cells.Select(c => c.Count()).Distinct().ToList();

        if (cellCounts.Count > 1 || cellCounts.Count == 0 || cellCounts[0] < 2)
        {
            blockers.Add("Gage R&R Crossed ANOVA requires a balanced design with identical repeat counts (>= 2) for all (Part, Operator) combinations.");
        }

        if (cells.Count != a * b)
        {
            blockers.Add($"Missing combinations: expected {a * b} (Part, Operator) cells, but found {cells.Count}. Crossed ANOVA requires a fully balanced matrix.");
        }

        if (blockers.Count > 0)
        {
            return CreateEmptyResult(featureName, blockers, warnings);
        }

        var r = cellCounts[0]; // repeats
        var totalN = a * b * r;

        // Calculate Means
        var ySum = data.Sum(x => x.Value);
        var grandMean = ySum / totalN;

        var partMeans = partGroups.ToDictionary(g => g.Key, g => g.Average(x => x.Value));
        var operMeans = operGroups.ToDictionary(g => g.Key, g => g.Average(x => x.Value));
        var cellMeans = cells.ToDictionary(g => g.Key, g => g.Average(x => x.Value));

        // Sums of Squares
        var ssTotal = data.Sum(x => Math.Pow(x.Value - grandMean, 2));
        var ssPart = b * r * partMeans.Values.Sum(m => Math.Pow(m - grandMean, 2));
        var ssOper = a * r * operMeans.Values.Sum(m => Math.Pow(m - grandMean, 2));

        var ssInteraction = 0.0;
        foreach (var cell in cells)
        {
            var pId = cell.Key.PartId;
            var oId = cell.Key.OperatorId;
            var cellM = cellMeans[cell.Key];
            var expected = partMeans[pId] + operMeans[oId] - grandMean;
            ssInteraction += r * Math.Pow(cellM - expected, 2);
        }

        var ssError = 0.0;
        foreach (var cell in cells)
        {
            var cellM = cellMeans[cell.Key];
            foreach (var item in cell)
            {
                ssError += Math.Pow(item.Value - cellM, 2);
            }
        }

        // Degrees of Freedom
        var dfPart = a - 1;
        var dfOper = b - 1;
        var dfInt = (a - 1) * (b - 1);
        var dfError = a * b * (r - 1);
        var dfTotal = totalN - 1;

        // Mean Squares
        var msPart = ssPart / dfPart;
        var msOper = ssOper / dfOper;
        var msInt = ssInteraction / dfInt;
        var msError = ssError / dfError;

        // Test interaction
        var fInt = msError > 1e-12 ? msInt / msError : 0.0;
        double pInt = 1.0;
        try
        {
            var fDist = new FisherSnedecor(dfInt, dfError);
            pInt = 1.0 - fDist.CumulativeDistribution(fInt);
        }
        catch
        {
            pInt = 0.0;
        }

        var interactionSignificant = pInt < alphaInteraction;
        var poolInteraction = !interactionSignificant;

        double msErrorUsed;
        int dfErrorUsed;
        double ssErrorUsed;

        double? fPart;
        double? pPart;
        double? fOper;
        double? pOper;

        if (poolInteraction)
        {
            warnings.Add($"Operator*Part interaction is not statistically significant (p = {pInt:F4} >= {alphaInteraction:F2}). Interaction has been pooled into repeatability error according to AIAG MSA standard.");
            ssErrorUsed = ssInteraction + ssError;
            dfErrorUsed = dfInt + dfError;
            msErrorUsed = ssErrorUsed / dfErrorUsed;

            fPart = msErrorUsed > 1e-12 ? msPart / msErrorUsed : null;
            fOper = msErrorUsed > 1e-12 ? msOper / msErrorUsed : null;

            pPart = fPart.HasValue ? ComputePValue(fPart.Value, dfPart, dfErrorUsed) : null;
            pOper = fOper.HasValue ? ComputePValue(fOper.Value, dfOper, dfErrorUsed) : null;
        }
        else
        {
            warnings.Add($"Operator*Part interaction is statistically significant (p = {pInt:F4} < {alphaInteraction:F2}). Measurement differences between operators depend on the part.");
            ssErrorUsed = ssError;
            dfErrorUsed = dfError;
            msErrorUsed = msError;

            fPart = msInt > 1e-12 ? msPart / msInt : null;
            fOper = msInt > 1e-12 ? msOper / msInt : null;

            pPart = fPart.HasValue ? ComputePValue(fPart.Value, dfPart, dfInt) : null;
            pOper = fOper.HasValue ? ComputePValue(fOper.Value, dfOper, dfInt) : null;
        }

        // ANOVA Table
        var anovaTable = new List<MsaAnovaRow>
        {
            new("Part", dfPart, ssPart, msPart, fPart, pPart),
            new("Operator", dfOper, ssOper, msOper, fOper, pOper)
        };

        if (!poolInteraction)
        {
            anovaTable.Add(new("Operator*Part", dfInt, ssInteraction, msInt, fInt, pInt));
            anovaTable.Add(new("Repeatability (Error)", dfError, ssError, msError, null, null));
        }
        else
        {
            anovaTable.Add(new("Repeatability (Pooled)", dfErrorUsed, ssErrorUsed, msErrorUsed, null, null));
        }
        anovaTable.Add(new("Total", dfTotal, ssTotal, ssTotal / dfTotal, null, null));

        // Variance components
        var varRepeatability = msErrorUsed;
        var varInteraction = poolInteraction ? 0.0 : Math.Max(0.0, (msInt - msError) / r);

        double varOperator;
        if (poolInteraction)
        {
            varOperator = Math.Max(0.0, (msOper - msErrorUsed) / (a * r));
        }
        else
        {
            varOperator = Math.Max(0.0, (msOper - msInt) / (a * r));
        }

        var varReproducibility = varOperator + varInteraction;
        var varGageRr = varRepeatability + varReproducibility;

        double varPart;
        if (poolInteraction)
        {
            varPart = Math.Max(0.0, (msPart - msErrorUsed) / (b * r));
        }
        else
        {
            varPart = Math.Max(0.0, (msPart - msInt) / (b * r));
        }

        var varTotal = varGageRr + varPart;
        if (varTotal <= 1e-12) varTotal = 1.0;

        var stdTotal = Math.Sqrt(varTotal);
        var stdGageRr = Math.Sqrt(varGageRr);
        var stdPart = Math.Sqrt(varPart);
        var stdRepeat = Math.Sqrt(varRepeatability);
        var stdReprod = Math.Sqrt(varReproducibility);
        var stdOper = Math.Sqrt(varOperator);
        var stdInt = Math.Sqrt(varInteraction);

        var ndc = stdGageRr > 1e-12 ? (int)Math.Floor(1.41 * (stdPart / stdGageRr)) : 1;
        if (ndc < 1) ndc = 1;

        var compList = new List<MsaVarianceComponent>
        {
            CreateComponent("Repeatability (EV)", varRepeatability, stdRepeat, varTotal, stdTotal, tolerance),
            CreateComponent("Reproducibility (AV)", varReproducibility, stdReprod, varTotal, stdTotal, tolerance),
            CreateComponent("Operator", varOperator, stdOper, varTotal, stdTotal, tolerance)
        };

        if (!poolInteraction)
        {
            compList.Add(CreateComponent("Operator*Part Interaction", varInteraction, stdInt, varTotal, stdTotal, tolerance));
        }

        compList.Add(CreateComponent("Total Gage R&R (GRR)", varGageRr, stdGageRr, varTotal, stdTotal, tolerance));
        compList.Add(CreateComponent("Part-To-Part (PV)", varPart, stdPart, varTotal, stdTotal, tolerance));
        compList.Add(CreateComponent("Total Variation (TV)", varTotal, stdTotal, varTotal, stdTotal, tolerance));

        var grrPercentStudyVar = 100.0 * (stdGageRr / stdTotal);
        string statusAssessment;
        if (grrPercentStudyVar < 10.0 && ndc >= 5)
        {
            statusAssessment = "Acceptable: Measurement system is capable (%GRR < 10%, ndc >= 5).";
        }
        else if (grrPercentStudyVar <= 30.0)
        {
            statusAssessment = $"Marginal: System may be acceptable (%GRR = {grrPercentStudyVar:F1}% in [10%, 30%], ndc = {ndc}).";
        }
        else
        {
            statusAssessment = $"Unacceptable: Measurement error is too high (%GRR = {grrPercentStudyVar:F1}% > 30%, ndc = {ndc}). Process improvement required.";
        }

        return new MsaResult(
            featureName,
            a,
            b,
            r,
            totalN,
            true,
            interactionSignificant,
            poolInteraction,
            ndc,
            statusAssessment,
            anovaTable,
            compList,
            blockers,
            warnings
        );
    }

    private static MsaVarianceComponent CreateComponent(
        string source,
        double variance,
        double stdDev,
        double totalVariance,
        double totalStdDev,
        double? tolerance)
    {
        var studyVar = 6.0 * stdDev;
        var pctContrib = totalVariance > 1e-12 ? 100.0 * (variance / totalVariance) : 0.0;
        var pctStudyVar = totalStdDev > 1e-12 ? 100.0 * (stdDev / totalStdDev) : 0.0;
        double? pctTolerance = null;

        if (tolerance.HasValue && tolerance.Value > 1e-12)
        {
            pctTolerance = 100.0 * (studyVar / tolerance.Value);
        }

        return new MsaVarianceComponent(
            source,
            variance,
            stdDev,
            studyVar,
            pctContrib,
            pctStudyVar,
            pctTolerance
        );
    }

    private static double? ComputePValue(double f, int df1, int df2)
    {
        try
        {
            var fDist = new FisherSnedecor(df1, df2);
            return Math.Max(0.0, 1.0 - fDist.CumulativeDistribution(f));
        }
        catch
        {
            return null;
        }
    }

    private static MsaResult CreateEmptyResult(string featureName, List<string> blockers, List<string> warnings)
    {
        return new MsaResult(
            featureName,
            0,
            0,
            0,
            0,
            false,
            false,
            false,
            0,
            "Evaluation Blocked",
            new List<MsaAnovaRow>(),
            new List<MsaVarianceComponent>(),
            blockers,
            warnings
        );
    }
}
