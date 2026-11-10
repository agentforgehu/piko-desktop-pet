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

    [Fact]
    public void OfflineEvaluatorProducesReproduciblePairedEvidenceAndTokenMetrics()
    {
        var request = OfflineRequest(CreateMeasurements(12));

        var report = OfflineCandidateEvaluator.Evaluate(request);
        var gate = ImprovementGate.Evaluate(report.Evaluation);
        var reordered = OfflineCandidateEvaluator.Evaluate(request with
        {
            Scenarios = request.Scenarios.Reverse().ToArray()
        });

        Assert.True(gate.EligibleForMaintainerReview);
        Assert.False(gate.MayChangeProduction);
        Assert.Equal(12, report.ScenarioCount);
        Assert.Equal(1200, report.BaselineInputTokens);
        Assert.Equal(1080, report.CandidateInputTokens);
        Assert.Equal(240, report.BaselineOutputTokens);
        Assert.Equal(216, report.CandidateOutputTokens);
        Assert.Equal(report.Evaluation.EvidenceDigest, reordered.Evaluation.EvidenceDigest);
        Assert.Matches("^[a-f0-9]{64}$", report.Evaluation.EvidenceDigest);
    }

    [Fact]
    public void OfflineEvaluatorRejectsAHiddenPerScenarioQualityRegression()
    {
        var measurements = CreateMeasurements(12)
            .Select((item, index) => item with { CandidateQuality = index == 0 ? 0.3 : 0.85 })
            .ToArray();
        var report = OfflineCandidateEvaluator.Evaluate(OfflineRequest(measurements));

        Assert.True(report.Evaluation.CandidateQuality > report.Evaluation.BaselineQuality);
        Assert.False(report.Evaluation.RegressionPassed);
        Assert.Contains("regression_gate_failed", ImprovementGate.Evaluate(report.Evaluation).Reasons);
    }

    [Fact]
    public void OfflineEvaluatorRejectsDuplicatesAndPersonalDataLikeIdentifiers()
    {
        var sample = CreateMeasurements(10)[0];
        Assert.Throws<ArgumentException>(() => OfflineCandidateEvaluator.Evaluate(
            OfflineRequest(new[] { sample, sample })));
        Assert.Throws<ArgumentException>(() => OfflineCandidateEvaluator.Evaluate(
            OfflineRequest(CreateMeasurements(10).Select(item => item with
            {
                ScenarioId = item.ScenarioId == "scenario-00" ? "user@example.com" : item.ScenarioId
            }).ToArray())));
    }

    private static OfflineComparisonRequest OfflineRequest(IReadOnlyList<CandidateScenarioMeasurement> scenarios) => new(
        "petmind-candidate-02",
        ImprovementRisk.Low,
        "baseline-01",
        "candidate-02",
        "holdout-2026-11-01",
        "evaluator-01",
        10,
        scenarios);

    private static IReadOnlyList<CandidateScenarioMeasurement> CreateMeasurements(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new CandidateScenarioMeasurement(
                $"scenario-{index:D2}",
                BaselineQuality: 0.8,
                CandidateQuality: 0.82,
                BaselineInputTokens: 100,
                CandidateInputTokens: 90,
                BaselineOutputTokens: 20,
                CandidateOutputTokens: 18,
                BaselineModelCalls: 1,
                CandidateModelCalls: 1,
                BaselineLatencyMilliseconds: 100,
                CandidateLatencyMilliseconds: 110,
                SafetyPassed: true,
                PrivacyPassed: true,
                RegressionPassed: true))
            .ToArray();

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
