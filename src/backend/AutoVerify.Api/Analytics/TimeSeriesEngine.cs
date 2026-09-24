namespace AutoVerify.Api.Analytics;

public sealed record EwmaPoint(
    int RowIndex,
    double RawValue,
    double EwmaValue,
    double Ucl,
    double Lcl,
    bool IsViolation,
    ObservationReference? Observation = null
);

public sealed record EwmaResult(
    string FeatureName,
    double Lambda,
    double L,
    double Target,
    double Sigma,
    double AsymptoticUcl,
    double AsymptoticLcl,
    int TotalSamples,
    int ViolationCount,
    List<EwmaPoint> Points
);

public sealed record CusumPoint(
    int RowIndex,
    double RawValue,
    double CusumHigh,
    double CusumLow,
    int RunLengthHigh,
    int RunLengthLow,
    bool IsViolationHigh,
    bool IsViolationLow,
    ObservationReference? Observation = null
);

public sealed record CusumResult(
    string FeatureName,
    double Target,
    double Sigma,
    double K,
    double H,
    int TotalSamples,
    int ViolationHighCount,
    int ViolationLowCount,
    List<CusumPoint> Points
);

public static class TimeSeriesEngine
{
    public static EwmaResult ComputeEwma(
        IReadOnlyList<(int RowIndex, double Value)> data,
        string featureName,
        double? lambda = 0.2,
        double? lFactor = 3.0,
        double? target = null,
        double? sigma = null)
    {
        if (data == null || data.Count == 0)
        {
            throw new ArgumentException("Data cannot be empty for EWMA computation.");
        }

        var lam = lambda ?? 0.2;
        if (lam <= 0 || lam > 1.0) lam = 0.2;

        var l = lFactor ?? 3.0;
        if (l <= 0) l = 3.0;

        var welford = new WelfordAccumulator();
        for (var i = 0; i < data.Count; i++)
        {
            welford.Add(data[i].Value);
        }

        var mu0 = target ?? welford.Mean;
        var s = sigma ?? (welford.StandardDeviation > 0 ? welford.StandardDeviation : 1.0);
        if (s <= 0) s = 1.0;

        var asymptoticSigma = s * Math.Sqrt(lam / (2.0 - lam));
        var asymptoticUcl = mu0 + l * asymptoticSigma;
        var asymptoticLcl = mu0 - l * asymptoticSigma;

        var points = new List<EwmaPoint>(data.Count);
        var prevZ = mu0;
        var violationCount = 0;

        for (var i = 0; i < data.Count; i++)
        {
            var raw = data[i].Value;
            var z = lam * raw + (1.0 - lam) * prevZ;
            prevZ = z;

            var step = i + 1;
            var timeVaryingSigma = s * Math.Sqrt((lam / (2.0 - lam)) * (1.0 - Math.Pow(1.0 - lam, 2 * step)));
            var ucl = mu0 + l * timeVaryingSigma;
            var lcl = mu0 - l * timeVaryingSigma;

            var isViolation = z > ucl || z < lcl;
            if (isViolation) violationCount++;

            points.Add(new EwmaPoint(data[i].RowIndex, raw, z, ucl, lcl, isViolation));
        }

        return new EwmaResult(
            featureName,
            lam,
            l,
            mu0,
            s,
            asymptoticUcl,
            asymptoticLcl,
            data.Count,
            violationCount,
            points
        );
    }

    public static CusumResult ComputeCusum(
        IReadOnlyList<(int RowIndex, double Value)> data,
        string featureName,
        double? target = null,
        double? sigma = null,
        double? kParam = 0.5,
        double? hParam = 5.0)
    {
        if (data == null || data.Count == 0)
        {
            throw new ArgumentException("Data cannot be empty for CUSUM computation.");
        }

        var welford = new WelfordAccumulator();
        for (var i = 0; i < data.Count; i++)
        {
            welford.Add(data[i].Value);
        }

        var mu0 = target ?? welford.Mean;
        var s = sigma ?? (welford.StandardDeviation > 0 ? welford.StandardDeviation : 1.0);
        if (s <= 0) s = 1.0;

        var kMultiplier = kParam ?? 0.5;
        if (kMultiplier <= 0) kMultiplier = 0.5;
        var hMultiplier = hParam ?? 5.0;
        if (hMultiplier <= 0) hMultiplier = 5.0;

        var k = kMultiplier * s;
        var h = hMultiplier * s;

        var points = new List<CusumPoint>(data.Count);
        var prevCPlus = 0.0;
        var prevCMinus = 0.0;
        var runLenPlus = 0;
        var runLenMinus = 0;
        var violHighCount = 0;
        var violLowCount = 0;

        for (var i = 0; i < data.Count; i++)
        {
            var raw = data[i].Value;

            var cPlus = Math.Max(0.0, raw - (mu0 + k) + prevCPlus);
            var cMinus = Math.Max(0.0, (mu0 - k) - raw + prevCMinus);

            runLenPlus = cPlus > 0 ? runLenPlus + 1 : 0;
            runLenMinus = cMinus > 0 ? runLenMinus + 1 : 0;

            var isViolHigh = cPlus > h;
            var isViolLow = cMinus > h;

            if (isViolHigh) violHighCount++;
            if (isViolLow) violLowCount++;

            points.Add(new CusumPoint(
                data[i].RowIndex,
                raw,
                cPlus,
                cMinus,
                runLenPlus,
                runLenMinus,
                isViolHigh,
                isViolLow));

            prevCPlus = cPlus;
            prevCMinus = cMinus;
        }

        return new CusumResult(
            featureName,
            mu0,
            s,
            k,
            h,
            data.Count,
            violHighCount,
            violLowCount,
            points
        );
    }
}
