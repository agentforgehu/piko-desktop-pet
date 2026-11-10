using Piko.Agent.Improvement;

namespace Piko.Agent.Tests;

public sealed class ImprovementGateTests
{
    [Fact]
    public void PassingOfflineCandidateIsReviewableButCannotChangeProduction()
    {
        var decision = ImprovementGate.Evaluate(ValidEvaluation());

        Assert.True(decision.EligibleForMaintainerReview);
        Assert.True(decision.RequiresHumanApproval);
        Assert.False(decision.MayChangeProduction);
        Assert.Empty(decision.Reasons);
    }

    [Theory]
    [InlineData(false, true, true, "safety_gate_failed")]
    [InlineData(true, false, true, "privacy_gate_failed")]
    [InlineData(true, true, false, "regression_gate_failed")]
    public void AnyHardGateFailureRejectsCandidate(
        bool safety,
        bool privacy,
        bool regression,
        string expectedReason)
    {
        var decision = ImprovementGate.Evaluate(ValidEvaluation() with
        {
            SafetyPassed = safety,
            PrivacyPassed = privacy,
            RegressionPassed = regression
        });

        Assert.False(decision.EligibleForMaintainerReview);
        Assert.False(decision.MayChangeProduction);
        Assert.Contains(expectedReason, decision.Reasons);
    }

    [Fact]
    public void QualityCostOrHoldoutRegressionBlocksReview()
    {
        var decision = ImprovementGate.Evaluate(ValidEvaluation() with
        {
            CandidateQuality = 0.79,
            CandidateCost = 120,
            HoldoutScenarioCount = 12
        });

        Assert.False(decision.EligibleForMaintainerReview);
        Assert.Contains("quality_regressed", decision.Reasons);
        Assert.Contains("cost_regression_exceeded", decision.Reasons);
        Assert.Contains("holdout_evidence_insufficient", decision.Reasons);
    }

    private static ImprovementEvaluation ValidEvaluation() => new(
        "memory-retrieval-ranker-001",
        ImprovementRisk.Low,
        BaselineQuality: 0.82,
        CandidateQuality: 0.84,
        BaselineCost: 100,
        CandidateCost: 103,
        HoldoutScenarioCount: 50,
        RequiredHoldoutScenarioCount: 40,
        SafetyPassed: true,
        PrivacyPassed: true,
        RegressionPassed: true,
        EvidenceDigest: new string('a', 64));
}
