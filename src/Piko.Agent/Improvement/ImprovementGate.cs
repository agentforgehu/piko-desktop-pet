namespace Piko.Agent.Improvement;

public enum ImprovementRisk
{
    Low,
    Medium,
    High
}

/// <summary>An offline comparison report. It contains no deployment action or code mutation capability.</summary>
public sealed record ImprovementEvaluation(
    string CandidateId,
    ImprovementRisk Risk,
    double BaselineQuality,
    double CandidateQuality,
    double BaselineCost,
    double CandidateCost,
    int HoldoutScenarioCount,
    int RequiredHoldoutScenarioCount,
    bool SafetyPassed,
    bool PrivacyPassed,
    bool RegressionPassed,
    string EvidenceDigest);

public sealed record ImprovementDecision(
    bool EligibleForMaintainerReview,
    bool RequiresHumanApproval,
    bool MayChangeProduction,
    IReadOnlyList<string> Reasons);

public static class ImprovementGate
{
    private static readonly IReadOnlyDictionary<ImprovementRisk, (int MinimumHoldout, double MaximumCostIncrease)> RiskBudgets =
        new Dictionary<ImprovementRisk, (int, double)>
        {
            [ImprovementRisk.Low] = (10, 0.05),
            [ImprovementRisk.Medium] = (30, 0.02),
            [ImprovementRisk.High] = (100, 0.0)
        };

    public static ImprovementDecision Evaluate(ImprovementEvaluation evaluation, double maximumCostIncrease = 0.05)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        var reasons = new List<string>();
        if (string.IsNullOrWhiteSpace(evaluation.CandidateId)) reasons.Add("candidate_id_missing");
        if (!Enum.IsDefined(evaluation.Risk)) reasons.Add("risk_level_invalid");
        var riskBudget = RiskBudgets.TryGetValue(evaluation.Risk, out var configuredBudget)
            ? configuredBudget
            : (MinimumHoldout: int.MaxValue, MaximumCostIncrease: 0.0);
        if (!InUnitInterval(evaluation.BaselineQuality) || !InUnitInterval(evaluation.CandidateQuality))
            reasons.Add("quality_score_invalid");
        if (!double.IsFinite(evaluation.BaselineCost) || evaluation.BaselineCost < 0 ||
            !double.IsFinite(evaluation.CandidateCost) || evaluation.CandidateCost < 0)
            reasons.Add("cost_score_invalid");
        if (evaluation.RequiredHoldoutScenarioCount < riskBudget.MinimumHoldout ||
            evaluation.HoldoutScenarioCount < evaluation.RequiredHoldoutScenarioCount)
            reasons.Add("holdout_evidence_insufficient");
        if (!IsDigest(evaluation.EvidenceDigest)) reasons.Add("evidence_digest_invalid");
        if (!evaluation.SafetyPassed) reasons.Add("safety_gate_failed");
        if (!evaluation.PrivacyPassed) reasons.Add("privacy_gate_failed");
        if (!evaluation.RegressionPassed) reasons.Add("regression_gate_failed");
        if (evaluation.CandidateQuality < evaluation.BaselineQuality)
            reasons.Add("quality_regressed");
        if (!double.IsFinite(maximumCostIncrease) || maximumCostIncrease < 0 || maximumCostIncrease > 1)
            reasons.Add("cost_budget_invalid");
        else if ((evaluation.BaselineCost == 0 && evaluation.CandidateCost > 0) ||
                 (evaluation.BaselineCost > 0 &&
                  evaluation.CandidateCost > evaluation.BaselineCost *
                  (1 + Math.Min(maximumCostIncrease, riskBudget.MaximumCostIncrease))))
            reasons.Add("cost_regression_exceeded");

        var eligible = reasons.Count == 0;
        return new ImprovementDecision(
            eligible,
            RequiresHumanApproval: eligible,
            MayChangeProduction: false,
            reasons);
    }

    private static bool InUnitInterval(double value) =>
        double.IsFinite(value) && value is >= 0 and <= 1;

    private static bool IsDigest(string value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
