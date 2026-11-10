using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Piko.Agent.Improvement;

/// <summary>A local, aggregate-only assessment record. It contains no prompt or response text.</summary>
public sealed record ImprovementLedgerEntry(
    int SchemaVersion,
    DateTimeOffset RecordedAtUtc,
    OfflineComparisonReport Report,
    ImprovementDecision Decision,
    ImprovementMaintainerReview? Review,
    string PreviousEntryDigest,
    string EntryDigest);

public enum ImprovementReviewOutcome
{
    AcceptedForManualIntegration,
    Rejected
}

/// <summary>The identity is an attribution supplied by the caller, not an authenticated identity.</summary>
public sealed record ImprovementMaintainerReview(
    string CandidateId,
    string EvidenceDigest,
    string ReviewerId,
    ImprovementReviewOutcome Outcome,
    string? RollbackVersion);

public sealed record ImprovementLedgerVerification(
    bool IsValid,
    int EntryCount,
    string LastEntryDigest,
    string Reason,
    ImprovementLedgerEntry? LastEntry = null);

/// <summary>
/// Appends hash-chained review evidence to a local JSONL file. The chain detects accidental or
/// partial edits, but is not a signature and cannot prove who wrote the file.
/// </summary>
public static class ImprovementLedger
{
    public const int CurrentSchemaVersion = 1;

    public static async Task<ImprovementLedgerEntry> AppendAsync(
        string path,
        OfflineComparisonReport report,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);
        ValidateReport(report);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var stream = new FileStream(
            fullPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var verification = await VerifyStreamAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!verification.IsValid)
        {
            throw new InvalidDataException($"Improvement ledger is invalid: {verification.Reason}");
        }

        var decision = ImprovementGate.Evaluate(report.Evaluation);
        var unsignedEntry = new ImprovementLedgerEntry(
            CurrentSchemaVersion,
            recordedAtUtc.ToUniversalTime(),
            report,
            decision,
            null,
            verification.LastEntryDigest,
            string.Empty);
        var entry = unsignedEntry with { EntryDigest = ComputeDigest(unsignedEntry) };
        await WriteEntryAsync(stream, entry, cancellationToken).ConfigureAwait(false);
        return entry;
    }

    public static async Task<ImprovementLedgerEntry> AppendReviewAsync(
        string path,
        string candidateId,
        string reviewerId,
        ImprovementReviewOutcome outcome,
        string? rollbackVersion,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateIdentifier(candidateId, nameof(candidateId));
        ValidateIdentifier(reviewerId, nameof(reviewerId));
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        if (outcome == ImprovementReviewOutcome.AcceptedForManualIntegration)
        {
            ValidateIdentifier(rollbackVersion ?? string.Empty, nameof(rollbackVersion));
        }
        else if (rollbackVersion is not null)
        {
            ValidateIdentifier(rollbackVersion, nameof(rollbackVersion));
        }

        var fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var verification = await VerifyStreamAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!verification.IsValid || verification.LastEntry is null)
        {
            throw new InvalidDataException($"Improvement ledger is invalid or empty: {verification.Reason}");
        }

        var previous = verification.LastEntry;
        if (previous.Review is not null ||
            !string.Equals(previous.Report.Evaluation.CandidateId, candidateId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The most recent ledger entry is not an unreviewed evaluation for this candidate.");
        }
        if (outcome == ImprovementReviewOutcome.AcceptedForManualIntegration &&
            !previous.Decision.EligibleForMaintainerReview)
        {
            throw new InvalidOperationException("A candidate that failed the hard gates cannot be accepted for manual integration.");
        }

        var review = new ImprovementMaintainerReview(
            candidateId,
            previous.Report.Evaluation.EvidenceDigest,
            reviewerId,
            outcome,
            rollbackVersion);
        var unsignedEntry = new ImprovementLedgerEntry(
            CurrentSchemaVersion,
            recordedAtUtc.ToUniversalTime(),
            previous.Report,
            previous.Decision,
            review,
            verification.LastEntryDigest,
            string.Empty);
        var entry = unsignedEntry with { EntryDigest = ComputeDigest(unsignedEntry) };
        await WriteEntryAsync(stream, entry, cancellationToken).ConfigureAwait(false);
        return entry;
    }

    public static async Task<ImprovementLedgerVerification> VerifyAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return new ImprovementLedgerVerification(false, 0, string.Empty, "ledger_missing");
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await VerifyStreamAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ImprovementLedgerVerification> VerifyStreamAsync(
        FileStream stream,
        CancellationToken cancellationToken)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var previousDigest = string.Empty;
        var count = 0;
        ImprovementLedgerEntry? previousEntry = null;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            count++;
            ImprovementLedgerEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<ImprovementLedgerEntry>(line);
            }
            catch (JsonException)
            {
                return new ImprovementLedgerVerification(false, count - 1, previousDigest, "entry_json_invalid");
            }

            if (entry is null || entry.SchemaVersion != CurrentSchemaVersion || entry.Report is null ||
                entry.Decision is null || string.IsNullOrWhiteSpace(entry.EntryDigest) || entry.EntryDigest.Length != 64 ||
                !string.Equals(entry.PreviousEntryDigest, previousDigest, StringComparison.Ordinal))
            {
                return new ImprovementLedgerVerification(false, count - 1, previousDigest, "entry_chain_invalid");
            }

            ImprovementDecision expectedDecision;
            try
            {
                ValidateReport(entry.Report);
                expectedDecision = ImprovementGate.Evaluate(entry.Report.Evaluation);
            }
            catch (ArgumentException)
            {
                return new ImprovementLedgerVerification(false, count - 1, previousDigest, "entry_report_invalid");
            }
            if (!DecisionsEqual(entry.Decision, expectedDecision) || entry.Decision.MayChangeProduction)
            {
                return new ImprovementLedgerVerification(false, count - 1, previousDigest, "entry_gate_decision_invalid");
            }

            if (entry.Review is not null &&
                !ReviewMatches(entry.Review, entry.Report, entry.Decision, previousEntry))
            {
                return new ImprovementLedgerVerification(false, count - 1, previousDigest, "entry_review_invalid");
            }

            var expectedDigest = ComputeDigest(entry with { EntryDigest = string.Empty });
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(entry.EntryDigest),
                    Encoding.ASCII.GetBytes(expectedDigest)))
            {
                return new ImprovementLedgerVerification(false, count - 1, previousDigest, "entry_digest_mismatch");
            }

            previousDigest = entry.EntryDigest;
            previousEntry = entry;
        }

        return new ImprovementLedgerVerification(true, count, previousDigest, "valid", previousEntry);
    }

    private static async Task WriteEntryAsync(
        FileStream stream,
        ImprovementLedgerEntry entry,
        CancellationToken cancellationToken)
    {
        var line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
        stream.Seek(0, SeekOrigin.End);
        await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static string ComputeDigest(ImprovementLedgerEntry entry)
    {
        var canonicalJson = JsonSerializer.Serialize(entry);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
    }

    private static void ValidateReport(OfflineComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(report.Evaluation);
        ValidateIdentifier(report.Evaluation.CandidateId, nameof(report));
        ValidateIdentifier(report.BaselineVersion, nameof(report));
        ValidateIdentifier(report.CandidateVersion, nameof(report));
        ValidateIdentifier(report.DatasetVersion, nameof(report));
        ValidateIdentifier(report.EvaluatorVersion, nameof(report));
        if (report.ScenarioCount < 0 || !double.IsFinite(report.BaselineMeanLatencyMilliseconds) ||
            !double.IsFinite(report.CandidateMeanLatencyMilliseconds))
        {
            throw new ArgumentException("Offline report contains invalid aggregate fields.", nameof(report));
        }
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            throw new ArgumentException("Ledger identifiers must be short, non-sensitive ASCII labels.", parameterName);
        }
    }

    private static bool ReviewMatches(
        ImprovementMaintainerReview review,
        OfflineComparisonReport report,
        ImprovementDecision decision,
        ImprovementLedgerEntry? previousEntry)
    {
        try
        {
            ValidateIdentifier(review.CandidateId, nameof(review));
            ValidateIdentifier(review.ReviewerId, nameof(review));
            if (!Enum.IsDefined(review.Outcome) || previousEntry is null || previousEntry.Review is not null ||
                !string.Equals(review.CandidateId, report.Evaluation.CandidateId, StringComparison.Ordinal) ||
                !string.Equals(review.EvidenceDigest, report.Evaluation.EvidenceDigest, StringComparison.Ordinal) ||
                !string.Equals(previousEntry.Report.Evaluation.EvidenceDigest, review.EvidenceDigest, StringComparison.Ordinal))
            {
                return false;
            }

            if (review.Outcome == ImprovementReviewOutcome.AcceptedForManualIntegration)
            {
                ValidateIdentifier(review.RollbackVersion ?? string.Empty, nameof(review));
                return decision.EligibleForMaintainerReview;
            }

            return review.RollbackVersion is null || IsValidIdentifier(review.RollbackVersion);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsValidIdentifier(string value)
    {
        try
        {
            ValidateIdentifier(value, nameof(value));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool DecisionsEqual(ImprovementDecision left, ImprovementDecision right) =>
        left.EligibleForMaintainerReview == right.EligibleForMaintainerReview &&
        left.RequiresHumanApproval == right.RequiresHumanApproval &&
        left.MayChangeProduction == right.MayChangeProduction &&
        left.Reasons.SequenceEqual(right.Reasons, StringComparer.Ordinal);
}
