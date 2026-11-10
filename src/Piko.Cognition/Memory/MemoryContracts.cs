namespace Piko.Cognition.Memory;

/// <summary>Host-neutral memory categories. Persisted values are part of the contract.</summary>
public enum MemoryCategory
{
    Working = 0,
    Episodic = 1,
    Semantic = 2,
    Profile = 3,
    Relationship = 4
}

public enum MemoryEvidenceSource
{
    ObservedEvent = 0,
    UserConfirmed = 1,
    ImportedKnowledge = 2,
    Hypothetical = 3
}

public enum MemorySensitivity
{
    Public = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Restricted = 4
}

/// <summary>Input to a store. Confidence is explicit and must describe evidence, not model certainty.</summary>
public sealed record CognitiveMemoryDraft(
    MemoryCategory Category,
    string Summary,
    string Content,
    MemorySensitivity Sensitivity,
    string Source,
    double Confidence,
    DateTimeOffset? ExpiresAt = null,
    MemoryEvidenceSource EvidenceSource = MemoryEvidenceSource.ObservedEvent,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidUntil = null,
    string? FactKey = null,
    string? FactValue = null);

public sealed record CognitiveMemoryRecord(
    Guid Id,
    MemoryCategory Category,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt,
    MemorySensitivity Sensitivity,
    string Source,
    string Summary,
    string Content,
    double Confidence,
    MemoryEvidenceSource EvidenceSource,
    bool IsSuperseded,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil,
    string? FactKey,
    string? FactValue);

public sealed record CognitiveMemoryHit(
    CognitiveMemoryRecord Record,
    double Score,
    double LexicalRelevance,
    double RecencyWeight,
    string ActivationReason);

/// <summary>Explicit recall constraints; cloud eligibility is decided by the host privacy policy.</summary>
public sealed record CognitiveMemoryRecallRequest(
    string Query,
    DateTimeOffset At,
    int Limit = 5,
    MemoryCategory? Category = null,
    MemorySensitivity MaximumSensitivity = MemorySensitivity.Low,
    bool IncludeHypothetical = false,
    bool IncludeSuperseded = false,
    bool OnlyCurrentlyValid = true);
