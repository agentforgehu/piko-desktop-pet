namespace Piko.Runtime.Ipc;

public sealed record RuntimeMemoryItem(
    string Id,
    string Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string Sensitivity,
    string Summary,
    string Provenance = "ObservedEvent");

public sealed record RuntimeMemoryListResponse(
    bool Available,
    string Reason,
    IReadOnlyList<RuntimeMemoryItem> Items);

public sealed record RuntimeMemoryAddRequest(string Kind, string Summary);
public sealed record RuntimeMemoryUpdateRequest(string Id, string Kind, string Summary);
public sealed record RuntimeMemoryDeleteRequest(string Id);
