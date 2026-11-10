namespace Piko.Runtime.Ipc;

public sealed record RuntimeMemoryItem(
    string Id,
    string Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt,
    string Sensitivity,
    string Summary,
    string Provenance = "ObservedEvent",
    bool IsSuperseded = false,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidUntil = null,
    string? FactKey = null,
    string? FactValue = null,
    string Relation = "none");

public sealed record RuntimeMemoryListResponse(
    bool Available,
    string Reason,
    IReadOnlyList<RuntimeMemoryItem> Items);

public sealed record RuntimeMemoryAddRequest(string Kind, string Summary, DateTimeOffset? ValidFrom = null, DateTimeOffset? ValidUntil = null, string? FactKey = null, string? FactValue = null);
public sealed record RuntimeMemoryRelatedRequest(string Kind, string Summary, string? FactKey = null, string? FactValue = null);
public sealed record RuntimeMemoryUpdateRequest(string Id, DateTimeOffset ExpectedUpdatedAt, string Kind, string Summary, DateTimeOffset? ValidFrom = null, DateTimeOffset? ValidUntil = null, string? FactKey = null, string? FactValue = null);
public sealed record RuntimeMemoryReplaceRequest(string Id, DateTimeOffset ExpectedUpdatedAt, string Kind, string Summary, DateTimeOffset? ValidFrom = null, DateTimeOffset? ValidUntil = null, string? FactKey = null, string? FactValue = null);
public sealed record RuntimeMemoryDeleteRequest(string Id);
