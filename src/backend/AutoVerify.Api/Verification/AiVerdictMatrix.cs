namespace AutoVerify.Api.Verification;

/// <summary>One defect image judged (or not) by the Master's AI. Status mirrors the Master's verify item status.</summary>
public sealed record AiDefectVerdict(string Status, string? Verdict);

public enum AiFinalStatus
{
    /// <summary>Station judged NG, AI judged every defect image OK: confirmed false alarm.</summary>
    OverkillConfirmed,
    /// <summary>Station judged OK/Pass, AI found at least one defect image NG: confirmed escape.</summary>
    LeakageConfirmed,
    /// <summary>AI found at least one NG defect image on an NG product.</summary>
    ConsistentNg,
    /// <summary>Station judged OK/Pass and AI judged every defect image OK.</summary>
    ConsistentOk,
    /// <summary>No usable AI answer for every image; the rule-based status stands and must be shown as such.</summary>
    AiUnavailable,
    /// <summary>Nothing to judge (no defect images); the rule-based status stands.</summary>
    NoImages
}

public static class AiVerdictMatrix
{
    /// <summary>
    /// Combines per-defect AI verdicts with the station judge. A single NG image is conclusive, but "all OK" needs every
    /// image answered: an unanswered image can never be counted as OK.
    /// </summary>
    public static AiFinalStatus Decide(bool stationJudgedNg, IReadOnlyCollection<AiDefectVerdict> defects)
    {
        if (defects.Count == 0) return AiFinalStatus.NoImages;

        var answered = defects.Where(d => d.Status == "Done" && d.Verdict is "OK" or "NG").ToList();
        var anyNg = answered.Any(d => d.Verdict == "NG");
        if (anyNg) return stationJudgedNg ? AiFinalStatus.ConsistentNg : AiFinalStatus.LeakageConfirmed;

        if (answered.Count < defects.Count) return AiFinalStatus.AiUnavailable;
        return stationJudgedNg ? AiFinalStatus.OverkillConfirmed : AiFinalStatus.ConsistentOk;
    }
}
