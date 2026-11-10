using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Piko.Agent.Models;

namespace Piko.Agent.Improvement;

/// <summary>A synthetic, privacy-safe test case with a simple, deterministic output oracle.</summary>
public sealed record OfflineFixtureScenario(
    string ScenarioId,
    AiModelRequest Request,
    IReadOnlyList<string> RequiredOutputFragments,
    IReadOnlyList<string> SafetyForbiddenFragments,
    IReadOnlyList<string> PrivacyForbiddenFragments);

/// <summary>A reviewed, versioned fixture document with an integrity digest over its canonical contents.</summary>
public sealed record OfflineFixtureDatasetManifest(
    string DatasetId,
    string DatasetVersion,
    string DatasetSha256,
    IReadOnlyList<OfflineFixtureScenario> Scenarios);

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
        string evaluatorVersion,
        IReadOnlyList<OfflineFixtureScenario> scenarios,
        CancellationToken cancellationToken = default,
        string datasetId = "adhoc",
        string datasetVersion = "unversioned",
        string? expectedDatasetSha256 = null)
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
        ValidateLabel(evaluatorVersion, nameof(evaluatorVersion));
        ValidateLabel(datasetId, nameof(datasetId));
        ValidateLabel(datasetVersion, nameof(datasetVersion));
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

        var orderedScenarios = scenarios.OrderBy(scenario => scenario.ScenarioId, StringComparer.Ordinal).ToArray();
        var datasetDigest = ComputeDatasetSha256(datasetId, datasetVersion, orderedScenarios);
        if (expectedDatasetSha256 is not null &&
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(datasetDigest),
                Encoding.ASCII.GetBytes(expectedDatasetSha256)))
        {
            throw new InvalidDataException("Fixture dataset content does not match its declared SHA-256.");
        }
        var measurements = new List<CandidateScenarioMeasurement>(orderedScenarios.Length);
        for (var index = 0; index < orderedScenarios.Length; index++)
        {
            var scenario = orderedScenarios[index];
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
            datasetDigest,
            evaluatorVersion,
            orderedScenarios.Length,
            measurements));
    }

    public static string ComputeDatasetSha256(
        string datasetId,
        string datasetVersion,
        IReadOnlyList<OfflineFixtureScenario> scenarios)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        ValidateLabel(datasetId, nameof(datasetId));
        ValidateLabel(datasetVersion, nameof(datasetVersion));
        if (scenarios.Count is < 10 or > OfflineCandidateEvaluator.MaximumScenarioCount)
        {
            throw new ArgumentException("Offline comparison requires 10 to 10,000 frozen scenarios.", nameof(scenarios));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in scenarios)
        {
            ValidateScenario(scenario);
            ValidateLabel(scenario.ScenarioId, nameof(scenarios));
            if (!ids.Add(scenario.ScenarioId))
            {
                throw new ArgumentException("Offline fixture identifiers must be unique.", nameof(scenarios));
            }
        }
        var ordered = scenarios.OrderBy(scenario => scenario.ScenarioId, StringComparer.Ordinal).ToArray();
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            DatasetId = datasetId,
            DatasetVersion = datasetVersion,
            Scenarios = ordered
        });
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
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
            scenario.Request is null || string.IsNullOrWhiteSpace(scenario.Request.SystemInstruction) ||
            scenario.Request.SystemInstruction.Length > 16_384 || scenario.Request.SanitizedContext is null ||
            scenario.Request.SanitizedContext.Length > 32_768 ||
            string.IsNullOrWhiteSpace(scenario.Request.UserRequest) || scenario.Request.UserRequest.Length > 8192 ||
            scenario.Request.MaximumOutputTokens is < 1 or > 4096 ||
            scenario.RequiredOutputFragments is null || scenario.RequiredOutputFragments.Count == 0 ||
            scenario.RequiredOutputFragments.Count > 64 ||
            scenario.RequiredOutputFragments.Any(string.IsNullOrWhiteSpace) ||
            scenario.SafetyForbiddenFragments is null || scenario.SafetyForbiddenFragments.Count == 0 ||
            scenario.SafetyForbiddenFragments.Count > 128 ||
            scenario.PrivacyForbiddenFragments is null || scenario.PrivacyForbiddenFragments.Count == 0 ||
            scenario.PrivacyForbiddenFragments.Count > 128 ||
            scenario.SafetyForbiddenFragments.Any(string.IsNullOrWhiteSpace) ||
            scenario.PrivacyForbiddenFragments.Any(string.IsNullOrWhiteSpace) ||
            scenario.RequiredOutputFragments.Any(fragment => fragment.Length > 256) ||
            scenario.SafetyForbiddenFragments.Any(fragment => fragment.Length > 256) ||
            scenario.PrivacyForbiddenFragments.Any(fragment => fragment.Length > 256))
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
