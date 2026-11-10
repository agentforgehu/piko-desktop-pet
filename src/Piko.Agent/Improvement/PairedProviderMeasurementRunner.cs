using System.Diagnostics;
using Piko.Agent.Models;

namespace Piko.Agent.Improvement;

/// <summary>A synthetic, privacy-safe test case with a simple, deterministic output oracle.</summary>
public sealed record OfflineFixtureScenario(
    string ScenarioId,
    AiModelRequest Request,
    IReadOnlyList<string> RequiredOutputFragments,
    IReadOnlyList<string> SafetyForbiddenFragments,
    IReadOnlyList<string> PrivacyForbiddenFragments);

/// <summary>
/// Executes the same frozen synthetic fixtures against loopback-only baseline and candidate
/// providers. It records only scalar measurements; prompts and generated text remain in memory.
/// </summary>
public static class PairedProviderMeasurementRunner
{
    public static async Task<OfflineComparisonReport> RunAsync(
        IOfflineAiProvider baseline,
        IOfflineAiProvider candidate,
        string candidateId,
        ImprovementRisk risk,
        string baselineVersion,
        string candidateVersion,
        string datasetVersion,
        string evaluatorVersion,
        IReadOnlyList<OfflineFixtureScenario> scenarios,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(scenarios);
        if (scenarios.Count is < 10 or > OfflineCandidateEvaluator.MaximumScenarioCount)
        {
            throw new ArgumentException("Offline comparison requires 10 to 10,000 frozen scenarios.", nameof(scenarios));
        }

        ValidateLabel(candidateId, nameof(candidateId));
        ValidateLabel(baselineVersion, nameof(baselineVersion));
        ValidateLabel(candidateVersion, nameof(candidateVersion));
        ValidateLabel(datasetVersion, nameof(datasetVersion));
        ValidateLabel(evaluatorVersion, nameof(evaluatorVersion));
        if (!Enum.IsDefined(risk))
        {
            throw new ArgumentOutOfRangeException(nameof(risk));
        }
        var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in scenarios)
        {
            ValidateScenario(scenario);
            ValidateLabel(scenario.ScenarioId, nameof(scenarios));
            if (!scenarioIds.Add(scenario.ScenarioId))
            {
                throw new ArgumentException("Offline fixture identifiers must be unique.", nameof(scenarios));
            }
        }

        var measurements = new List<CandidateScenarioMeasurement>(scenarios.Count);
        for (var index = 0; index < scenarios.Count; index++)
        {
            var scenario = scenarios[index];
            cancellationToken.ThrowIfCancellationRequested();
            (AiModelResponse Response, AiTokenUsage Usage, double LatencyMilliseconds) baselineResult;
            (AiModelResponse Response, AiTokenUsage Usage, double LatencyMilliseconds) candidateResult;
            if (index % 2 == 0)
            {
                baselineResult = await MeasureAsync(baseline, scenario.Request, cancellationToken).ConfigureAwait(false);
                candidateResult = await MeasureAsync(candidate, scenario.Request, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                candidateResult = await MeasureAsync(candidate, scenario.Request, cancellationToken).ConfigureAwait(false);
                baselineResult = await MeasureAsync(baseline, scenario.Request, cancellationToken).ConfigureAwait(false);
            }
            var baselineQuality = Score(baselineResult.Response.Text, scenario.RequiredOutputFragments);
            var candidateQuality = Score(candidateResult.Response.Text, scenario.RequiredOutputFragments);
            measurements.Add(new CandidateScenarioMeasurement(
                scenario.ScenarioId,
                baselineQuality,
                candidateQuality,
                baselineResult.Usage.InputTokens,
                candidateResult.Usage.InputTokens,
                baselineResult.Usage.OutputTokens,
                candidateResult.Usage.OutputTokens,
                1,
                1,
                baselineResult.LatencyMilliseconds,
                candidateResult.LatencyMilliseconds,
                IsSafe(baselineResult.Response.Text, scenario.SafetyForbiddenFragments) &&
                IsSafe(candidateResult.Response.Text, scenario.SafetyForbiddenFragments),
                IsSafe(baselineResult.Response.Text, scenario.PrivacyForbiddenFragments) &&
                IsSafe(candidateResult.Response.Text, scenario.PrivacyForbiddenFragments),
                candidateQuality >= baselineQuality));
        }

        return OfflineCandidateEvaluator.Evaluate(new OfflineComparisonRequest(
            candidateId,
            risk,
            baselineVersion,
            candidateVersion,
            datasetVersion,
            evaluatorVersion,
            scenarios.Count,
            measurements));
    }

    private static async Task<(AiModelResponse Response, AiTokenUsage Usage, double LatencyMilliseconds)> MeasureAsync(
        IOfflineAiProvider provider,
        AiModelRequest request,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var response = await provider.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (!response.Available || response.TokenUsage is null)
        {
            throw new InvalidDataException("Offline provider failed or did not return exact token usage; no report was produced.");
        }
        return (response, response.TokenUsage, elapsed);
    }

    private static double Score(string output, IReadOnlyList<string> required) =>
        required.Count(fragment => output.Contains(fragment, StringComparison.OrdinalIgnoreCase)) / (double)required.Count;

    private static bool IsSafe(string output, IReadOnlyList<string> forbidden) =>
        forbidden.All(fragment => !output.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static void ValidateScenario(OfflineFixtureScenario scenario)
    {
        if (scenario is null || string.IsNullOrWhiteSpace(scenario.ScenarioId) ||
            scenario.Request is null || string.IsNullOrWhiteSpace(scenario.Request.UserRequest) ||
            scenario.RequiredOutputFragments is null || scenario.RequiredOutputFragments.Count == 0 ||
            scenario.RequiredOutputFragments.Any(string.IsNullOrWhiteSpace) ||
            scenario.SafetyForbiddenFragments is null || scenario.SafetyForbiddenFragments.Count == 0 ||
            scenario.PrivacyForbiddenFragments is null || scenario.PrivacyForbiddenFragments.Count == 0 ||
            scenario.SafetyForbiddenFragments.Any(string.IsNullOrWhiteSpace) ||
            scenario.PrivacyForbiddenFragments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Offline fixtures need safe identifiers, a request, and deterministic quality/safety/privacy checks.", nameof(scenario));
        }
    }

    private static void ValidateLabel(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            throw new ArgumentException("Offline labels must be short, non-sensitive ASCII identifiers.", parameterName);
        }
    }
}
