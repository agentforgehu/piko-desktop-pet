using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Piko.Agent.Improvement;

/// <summary>
/// A paired, aggregate-only measurement for one frozen holdout scenario.
/// Do not put prompts, replies, user memory, or other personal data in this record.
/// </summary>
public sealed record CandidateScenarioMeasurement(
    string ScenarioId,
    double BaselineQuality,
    double CandidateQuality,
    int BaselineInputTokens,
    int CandidateInputTokens,
    int BaselineOutputTokens,
    int CandidateOutputTokens,
    int BaselineModelCalls,
    int CandidateModelCalls,
    double BaselineLatencyMilliseconds,
    double CandidateLatencyMilliseconds,
    bool SafetyPassed,
    bool PrivacyPassed,
    bool RegressionPassed);

public sealed record OfflineComparisonRequest(
    string CandidateId,
    ImprovementRisk Risk,
    string BaselineVersion,
    string CandidateVersion,
    string DatasetVersion,
    string EvaluatorVersion,
    int RequiredHoldoutScenarioCount,
    IReadOnlyList<CandidateScenarioMeasurement> Scenarios);

public sealed record OfflineComparisonReport(
    ImprovementEvaluation Evaluation,
    string BaselineVersion,
    string CandidateVersion,
    string DatasetVersion,
    string EvaluatorVersion,
    int ScenarioCount,
    long BaselineInputTokens,
    long CandidateInputTokens,
    long BaselineOutputTokens,
    long CandidateOutputTokens,
    long BaselineModelCalls,
    long CandidateModelCalls,
    double BaselineMeanLatencyMilliseconds,
    double CandidateMeanLatencyMilliseconds);

/// <summary>
/// Compares baseline and candidate on identical frozen scenarios. It evaluates evidence only;
/// it has no access to production configuration, prompts, deployment, or code mutation.
/// </summary>
public static class OfflineCandidateEvaluator
{
    private const double MaximumLatencyIncrease = 0.20;

    public static OfflineComparisonReport Evaluate(OfflineComparisonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateVersion(request.CandidateId, nameof(request.CandidateId));
        ValidateVersion(request.BaselineVersion, nameof(request.BaselineVersion));
        ValidateVersion(request.CandidateVersion, nameof(request.CandidateVersion));
        ValidateVersion(request.DatasetVersion, nameof(request.DatasetVersion));
        ValidateVersion(request.EvaluatorVersion, nameof(request.EvaluatorVersion));
        ArgumentNullException.ThrowIfNull(request.Scenarios);
        if (request.Scenarios.Count == 0)
        {
            throw new ArgumentException("At least one paired holdout measurement is required.", nameof(request));
        }

        if (request.Scenarios.Any(item => item is null))
        {
            throw new ArgumentException("Holdout measurements cannot contain null entries.", nameof(request));
        }

        var scenarios = request.Scenarios.OrderBy(item => item.ScenarioId, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < scenarios.Length; index++)
        {
            ValidateMeasurement(scenarios[index], nameof(request));
            if (index > 0 && string.Equals(scenarios[index - 1].ScenarioId, scenarios[index].ScenarioId, StringComparison.Ordinal))
            {
                throw new ArgumentException("Holdout scenario identifiers must be unique.", nameof(request));
            }
        }

        var baselineQuality = scenarios.Average(item => item.BaselineQuality);
        var candidateQuality = scenarios.Average(item => item.CandidateQuality);
        var baselineInput = scenarios.Sum(item => (long)item.BaselineInputTokens);
        var candidateInput = scenarios.Sum(item => (long)item.CandidateInputTokens);
        var baselineOutput = scenarios.Sum(item => (long)item.BaselineOutputTokens);
        var candidateOutput = scenarios.Sum(item => (long)item.CandidateOutputTokens);
        var baselineCalls = scenarios.Sum(item => (long)item.BaselineModelCalls);
        var candidateCalls = scenarios.Sum(item => (long)item.CandidateModelCalls);
        var baselineLatency = scenarios.Average(item => item.BaselineLatencyMilliseconds);
        var candidateLatency = scenarios.Average(item => item.CandidateLatencyMilliseconds);

        var perScenarioNonRegression = scenarios.All(item =>
            item.CandidateQuality >= item.BaselineQuality &&
            item.CandidateModelCalls <= item.BaselineModelCalls &&
            WithinLatencyBudget(item.BaselineLatencyMilliseconds, item.CandidateLatencyMilliseconds));
        var aggregateNonRegression = candidateQuality >= baselineQuality &&
                                     candidateCalls <= baselineCalls &&
                                     WithinLatencyBudget(baselineLatency, candidateLatency);
        var evidenceDigest = ComputeEvidenceDigest(request, scenarios);
        var evaluation = new ImprovementEvaluation(
            request.CandidateId,
            request.Risk,
            baselineQuality,
            candidateQuality,
            (baselineInput + baselineOutput) / (double)scenarios.Length,
            (candidateInput + candidateOutput) / (double)scenarios.Length,
            scenarios.Length,
            request.RequiredHoldoutScenarioCount,
            scenarios.All(item => item.SafetyPassed),
            scenarios.All(item => item.PrivacyPassed),
            perScenarioNonRegression && aggregateNonRegression && scenarios.All(item => item.RegressionPassed),
            evidenceDigest);

        return new OfflineComparisonReport(
            evaluation,
            request.BaselineVersion,
            request.CandidateVersion,
            request.DatasetVersion,
            request.EvaluatorVersion,
            scenarios.Length,
            baselineInput,
            candidateInput,
            baselineOutput,
            candidateOutput,
            baselineCalls,
            candidateCalls,
            baselineLatency,
            candidateLatency);
    }

    private static string ComputeEvidenceDigest(
        OfflineComparisonRequest request,
        IReadOnlyList<CandidateScenarioMeasurement> scenarios)
    {
        // Stable ordering and aggregate-only fields make this digest reproducible without
        // copying sensitive prompts or response bodies into an audit artifact.
        var evidence = JsonSerializer.Serialize(new
        {
            request.CandidateId,
            request.Risk,
            request.BaselineVersion,
            request.CandidateVersion,
            request.DatasetVersion,
            request.EvaluatorVersion,
            request.RequiredHoldoutScenarioCount,
            Scenarios = scenarios
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence))).ToLowerInvariant();
    }

    private static void ValidateMeasurement(CandidateScenarioMeasurement measurement, string parameterName)
    {
        ValidateVersion(measurement.ScenarioId, parameterName);
        if (!InUnitInterval(measurement.BaselineQuality) || !InUnitInterval(measurement.CandidateQuality) ||
            measurement.BaselineInputTokens < 0 || measurement.CandidateInputTokens < 0 ||
            measurement.BaselineOutputTokens < 0 || measurement.CandidateOutputTokens < 0 ||
            measurement.BaselineModelCalls < 0 || measurement.CandidateModelCalls < 0 ||
            !double.IsFinite(measurement.BaselineLatencyMilliseconds) || measurement.BaselineLatencyMilliseconds < 0 ||
            !double.IsFinite(measurement.CandidateLatencyMilliseconds) || measurement.CandidateLatencyMilliseconds < 0)
        {
            throw new ArgumentException("Holdout measurements contain invalid quality, cost, call, or latency values.", parameterName);
        }
    }

    private static void ValidateVersion(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            throw new ArgumentException("Identifiers must be short, non-sensitive ASCII labels.", parameterName);
        }
    }

    private static bool InUnitInterval(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

    private static bool WithinLatencyBudget(double baseline, double candidate) =>
        baseline == 0 ? candidate == 0 : candidate <= baseline * (1 + MaximumLatencyIncrease);
}
