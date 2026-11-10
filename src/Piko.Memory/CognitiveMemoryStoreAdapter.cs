using Piko.Cognition.Memory;
using Piko.Context.Events;

namespace Piko.Memory;

/// <summary>
/// Adapts the encrypted SQLite implementation to the host-neutral cognition contract.
/// The caller owns and disposes the supplied storage instance.
/// </summary>
public sealed class CognitiveMemoryStoreAdapter(SqliteMemoryStore storage) : ICognitiveMemoryStore
{
    private readonly SqliteMemoryStore _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    public async Task<CognitiveMemoryRecord> AddAsync(
        CognitiveMemoryDraft draft,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var record = await _storage.AddAsync(ToStorageDraft(draft), now, cancellationToken).ConfigureAwait(false);
        return ToCognitionRecord(record);
    }

    public async Task<CognitiveMemoryRecord?> GetAsync(
        Guid id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var record = await _storage.GetAsync(id, now, cancellationToken).ConfigureAwait(false);
        return record is null ? null : ToCognitionRecord(record);
    }

    public async Task<IReadOnlyList<CognitiveMemoryRecord>> ListAsync(
        DateTimeOffset now,
        MemoryCategory? category = null,
        int limit = 100,
        MemorySensitivity? maximumSensitivity = null,
        bool includeSuperseded = true,
        bool onlyCurrentlyValid = false,
        CancellationToken cancellationToken = default)
    {
        var records = await _storage.ListAsync(
            now,
            category is null ? null : ToStorage(category.Value),
            limit,
            cancellationToken,
            maximumSensitivity is null ? null : ToStorage(maximumSensitivity.Value),
            includeSuperseded,
            onlyCurrentlyValid).ConfigureAwait(false);
        return records.Select(ToCognitionRecord).ToArray();
    }

    public async Task<IReadOnlyList<CognitiveMemoryRecord>> ExportAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var records = await _storage.ListAsync(
            now,
            limit: 10_000,
            cancellationToken: cancellationToken,
            includeSuperseded: true,
            onlyCurrentlyValid: false,
            includeExpired: true).ConfigureAwait(false);
        return records.Select(ToCognitionRecord).ToArray();
    }

    public async Task<IReadOnlyList<CognitiveMemoryHit>> RecallAsync(
        CognitiveMemoryRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Query);
        if (request.Limit is < 1 or > 20 || !Enum.IsDefined(request.MaximumSensitivity) ||
            (request.Category is { } category && !Enum.IsDefined(category)))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Recall bounds or filters are invalid.");
        }

        var hits = await _storage.SearchAsync(
            request.Query,
            request.At,
            request.Limit,
            cancellationToken,
            request.Category is null ? null : ToStorage(request.Category.Value),
            ToStorage(request.MaximumSensitivity),
            request.IncludeHypothetical,
            request.IncludeSuperseded,
            request.OnlyCurrentlyValid).ConfigureAwait(false);
        return hits.Select(hit => new CognitiveMemoryHit(
            ToCognitionRecord(hit.Record),
            hit.Score,
            hit.LexicalRelevance,
            hit.RecencyWeight,
            hit.ActivationReason)).ToArray();
    }

    public async Task<IReadOnlyList<CognitiveMemoryHit>> FindRelatedAsync(
        MemoryCategory category,
        string summary,
        DateTimeOffset now,
        int limit = 3,
        string? factKey = null,
        string? factValue = null,
        CancellationToken cancellationToken = default)
    {
        var hits = await _storage.FindRelatedAsync(
            ToStorage(category), summary, now, limit, cancellationToken, factKey, factValue).ConfigureAwait(false);
        return hits.Select(hit => new CognitiveMemoryHit(
            ToCognitionRecord(hit.Record), hit.Score, hit.LexicalRelevance,
            hit.RecencyWeight, hit.ActivationReason)).ToArray();
    }

    public async Task<CognitiveMemoryRecord?> ReplaceAsync(
        Guid priorId,
        DateTimeOffset expectedUpdatedAt,
        CognitiveMemoryDraft replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var record = await _storage.ReplaceAsync(
            priorId, expectedUpdatedAt, ToStorageDraft(replacement), now, cancellationToken).ConfigureAwait(false);
        return record is null ? null : ToCognitionRecord(record);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _storage.DeleteAsync(id, cancellationToken);

    public Task<int> DeleteAllAsync(CancellationToken cancellationToken = default) =>
        _storage.DeleteAllAsync(cancellationToken);

    private static MemoryDraft ToStorageDraft(CognitiveMemoryDraft draft) => new(
        ToStorage(draft.Category),
        draft.Summary,
        draft.Content,
        ToStorage(draft.Sensitivity),
        draft.Source,
        draft.ExpiresAt,
        ToStorage(draft.EvidenceSource),
        draft.ValidFrom,
        draft.ValidUntil,
        draft.FactKey,
        draft.FactValue,
        draft.Confidence);

    private static CognitiveMemoryRecord ToCognitionRecord(MemoryRecord record) => new(
        record.Id,
        (MemoryCategory)record.Kind,
        record.CreatedAt,
        record.UpdatedAt,
        record.ExpiresAt,
        (MemorySensitivity)record.Sensitivity,
        record.Source,
        record.Summary,
        record.Payload,
        record.Confidence,
        (MemoryEvidenceSource)record.Provenance,
        record.IsSuperseded,
        record.ValidFrom,
        record.ValidUntil,
        record.FactKey,
        record.FactValue);

    private static MemoryKind ToStorage(MemoryCategory value) =>
        Enum.IsDefined(value) ? (MemoryKind)value : throw new ArgumentOutOfRangeException(nameof(value));

    private static DataSensitivity ToStorage(MemorySensitivity value) =>
        Enum.IsDefined(value) ? (DataSensitivity)value : throw new ArgumentOutOfRangeException(nameof(value));

    private static MemoryProvenance ToStorage(MemoryEvidenceSource value) =>
        Enum.IsDefined(value) ? (MemoryProvenance)value : throw new ArgumentOutOfRangeException(nameof(value));
}
