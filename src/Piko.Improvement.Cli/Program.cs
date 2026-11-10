using System.Text.Json;
using System.Text.Json.Serialization;
using Piko.Agent.Improvement;
using Piko.Agent.Models;

public static class Program
{
    private const int MaximumInputBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions InputOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && string.Equals(args[0], "evaluate", StringComparison.Ordinal))
            {
                return await EvaluateAsync(args[1], args[2]).ConfigureAwait(false);
            }
            if (args.Length == 5 && string.Equals(args[0], "seal-fixtures", StringComparison.Ordinal))
            {
                return await SealFixturesAsync(args[1], args[2], args[3], args[4]).ConfigureAwait(false);
            }
            if (args.Length is 10 or 11 && string.Equals(args[0], "run-local", StringComparison.Ordinal))
            {
                return await RunLocalAsync(args).ConfigureAwait(false);
            }
            if (args.Length is 5 or 6 && string.Equals(args[0], "review", StringComparison.Ordinal))
            {
                return await ReviewAsync(args).ConfigureAwait(false);
            }

            Console.Error.WriteLine(
                "Usage: piko-improvement evaluate <paired-measurements.json> <local-ledger.jsonl>\n" +
                "   or: piko-improvement seal-fixtures <fixture-array.json> <dataset-id> <dataset-version> <versioned-fixture-manifest.json>\n" +
                "   or: piko-improvement run-local <versioned-fixture-manifest.json> <local-ledger.jsonl> <candidate-id> <risk> <baseline-version> <candidate-version> <evaluator-version> <baseline-model> <candidate-model> [loopback-endpoint]\n" +
                "   or: piko-improvement review <local-ledger.jsonl> <candidate-id> <reviewer-id> <accept|reject> [rollback-version]");
            return 2;
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Improvement command failed: {exception.Message}");
            return 2;
        }
    }

    private static async Task<int> SealFixturesAsync(
        string inputPath,
        string datasetId,
        string datasetVersion,
        string outputPath)
    {
        var inputInfo = new FileInfo(inputPath);
        if (!inputInfo.Exists || inputInfo.Length is 0 or > OfflineFixtureDatasetManifestLoader.MaximumManifestBytes)
        {
            throw new InvalidDataException($"Fixture source must be between 1 and {OfflineFixtureDatasetManifestLoader.MaximumManifestBytes} bytes.");
        }
        var scenarios = JsonSerializer.Deserialize<OfflineFixtureScenario[]>(
            await File.ReadAllBytesAsync(inputPath).ConfigureAwait(false), InputOptions)
            ?? throw new InvalidDataException("Fixture source is empty or invalid.");
        var manifest = OfflineFixtureDatasetManifestLoader.Create(datasetId, datasetVersion, scenarios);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(manifest, OutputOptions)).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            datasetId = manifest.DatasetId,
            datasetVersion = manifest.DatasetVersion,
            datasetSha256 = manifest.DatasetSha256,
            scenarioCount = manifest.Scenarios.Count,
            versionControlReviewRequired = true
        }, OutputOptions));
        return 0;
    }

    private static async Task<int> RunLocalAsync(string[] args)
    {
        var inputInfo = new FileInfo(args[1]);
        if (!inputInfo.Exists || inputInfo.Length is 0 or > OfflineFixtureDatasetManifestLoader.MaximumManifestBytes)
        {
            throw new InvalidDataException($"Fixture manifest must be between 1 and {OfflineFixtureDatasetManifestLoader.MaximumManifestBytes} bytes.");
        }
        if (!Enum.TryParse<ImprovementRisk>(args[4], ignoreCase: false, out var risk) || !Enum.IsDefined(risk))
        {
            throw new ArgumentException("Risk must be Low, Medium, or High.");
        }

        var endpoint = args.Length == 11 ? new Uri(args[10], UriKind.Absolute) : new Uri("http://127.0.0.1:11434/v1/");
        var providerOptions = new OpenAiCompatibleChatOptions { Endpoint = endpoint };
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        var baseline = new OpenAiCompatibleChatProvider(httpClient, providerOptions with { Model = args[8] });
        var candidate = new OpenAiCompatibleChatProvider(httpClient, providerOptions with { Model = args[9] });
        var manifest = OfflineFixtureDatasetManifestLoader.Parse(
            await File.ReadAllBytesAsync(args[1]).ConfigureAwait(false));
        var report = await PairedProviderMeasurementRunner.RunAsync(
            baseline,
            candidate,
            args[3],
            risk,
            args[5],
            args[6],
            args[7],
            manifest.Scenarios,
            datasetId: manifest.DatasetId,
            datasetVersion: manifest.DatasetVersion,
            expectedDatasetSha256: manifest.DatasetSha256).ConfigureAwait(false);
        var entry = await ImprovementLedger.AppendAsync(args[2], report, DateTimeOffset.UtcNow).ConfigureAwait(false);
        var verification = await ImprovementLedger.VerifyAsync(args[2]).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            report = entry.Report,
            decision = entry.Decision,
            ledger = new { entryCount = verification.EntryCount, valid = verification.IsValid, entryDigest = entry.EntryDigest },
            dataset = new { manifest.DatasetId, manifest.DatasetVersion, sha256 = manifest.DatasetSha256 },
            fixtureTextPersisted = false,
            providerEndpoint = endpoint.GetLeftPart(UriPartial.Authority),
            productionMutationAvailable = false
        }, OutputOptions));
        return verification.IsValid ? 0 : 3;
    }

    private static async Task<int> EvaluateAsync(string inputPath, string ledgerPath)
    {
        var inputInfo = new FileInfo(inputPath);
        if (!inputInfo.Exists || inputInfo.Length is 0 or > MaximumInputBytes)
        {
            throw new InvalidDataException($"Input must be between 1 and {MaximumInputBytes} bytes.");
        }
        var bytes = await File.ReadAllBytesAsync(inputPath).ConfigureAwait(false);
        var request = JsonSerializer.Deserialize<OfflineComparisonRequest>(bytes, InputOptions)
            ?? throw new InvalidDataException("Measurement document is empty or invalid.");
        var report = OfflineCandidateEvaluator.Evaluate(request);
        var entry = await ImprovementLedger.AppendAsync(ledgerPath, report, DateTimeOffset.UtcNow)
            .ConfigureAwait(false);
        var verification = await ImprovementLedger.VerifyAsync(ledgerPath).ConfigureAwait(false);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            report = entry.Report,
            decision = entry.Decision,
            ledger = new
            {
                entryCount = verification.EntryCount,
                valid = verification.IsValid,
                entryDigest = entry.EntryDigest
            },
            productionMutationAvailable = false
        }, OutputOptions));
        return verification.IsValid ? 0 : 3;
    }

    private static async Task<int> ReviewAsync(string[] args)
    {
        var outcome = args[4] switch
        {
            "accept" => ImprovementReviewOutcome.AcceptedForManualIntegration,
            "reject" => ImprovementReviewOutcome.Rejected,
            _ => throw new ArgumentException("Review outcome must be accept or reject.", nameof(args))
        };
        var rollbackVersion = args.Length == 6 ? args[5] : null;
        var entry = await ImprovementLedger.AppendReviewAsync(
            args[1], args[2], args[3], outcome, rollbackVersion, DateTimeOffset.UtcNow).ConfigureAwait(false);
        var verification = await ImprovementLedger.VerifyAsync(args[1]).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            review = entry.Review,
            reviewIdentityAuthenticated = false,
            ledgerValid = verification.IsValid,
            productionMutationAvailable = false
        }, OutputOptions));
        return verification.IsValid ? 0 : 3;
    }
}
