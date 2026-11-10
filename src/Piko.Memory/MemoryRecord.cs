using Piko.Context.Events;

namespace Piko.Memory;

public sealed record MemoryDraft(
    MemoryKind Kind,
    string Summary,
    string Payload,
    DataSensitivity Sensitivity,
    string Source,
    DateTimeOffset? ExpiresAt = null,
    MemoryProvenance Provenance = MemoryProvenance.ObservedEvent);

public sealed record MemoryRecord(
    Guid Id,
    MemoryKind Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt,
    DataSensitivity Sensitivity,
    string Source,
    string Summary,
    string Payload,
    MemoryProvenance Provenance = MemoryProvenance.ObservedEvent,
    bool IsSuperseded = false);

public sealed record MemorySearchHit(
    MemoryRecord Record,
    double Score,
    double LexicalRelevance,
    double RecencyWeight,
    string ActivationReason);
