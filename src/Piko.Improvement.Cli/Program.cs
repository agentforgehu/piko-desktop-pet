using System.Text.Json;
using System.Text.Json.Serialization;
using Piko.Agent.Improvement;

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
            if (args.Length is 5 or 6 && string.Equals(args[0], "review", StringComparison.Ordinal))
            {
                return await ReviewAsync(args).ConfigureAwait(false);
            }

            Console.Error.WriteLine(
                "Usage: piko-improvement evaluate <paired-measurements.json> <local-ledger.jsonl>\n" +
                "   or: piko-improvement review <local-ledger.jsonl> <candidate-id> <reviewer-id> <accept|reject> [rollback-version]");
            return 2;
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Improvement command failed: {exception.Message}");
            return 2;
        }
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
