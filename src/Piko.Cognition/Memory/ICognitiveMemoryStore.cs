namespace Piko.Cognition.Memory;

/// <summary>
/// Storage-neutral memory contract for cognition modules. Implementations own encryption,
/// persistence, deletion semantics, and filtering before returning records.
/// </summary>
public interface ICognitiveMemoryStore
{
    Task<CognitiveMemoryRecord> AddAsync(
        CognitiveMemoryDraft draft,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<CognitiveMemoryRecord?> GetAsync(
        Guid id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CognitiveMemoryRecord>> ListAsync(
        DateTimeOffset now,
        MemoryCategory? category = null,
        int limit = 100,
        MemorySensitivity? maximumSensitivity = null,
        bool includeSuperseded = true,
        bool onlyCurrentlyValid = false,
        CancellationToken cancellationToken = default);

    /// <summary>Exports all retained records, including expired and superseded entries, for explicit user backup.</summary>
    Task<IReadOnlyList<CognitiveMemoryRecord>> ExportAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CognitiveMemoryHit>> RecallAsync(
        CognitiveMemoryRecallRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CognitiveMemoryHit>> FindRelatedAsync(
        MemoryCategory category,
        string summary,
        DateTimeOffset now,
        int limit = 3,
        string? factKey = null,
        string? factValue = null,
        CancellationToken cancellationToken = default);

    Task<CognitiveMemoryRecord?> ReplaceAsync(
        Guid priorId,
        DateTimeOffset expectedUpdatedAt,
        CognitiveMemoryDraft replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);
}
