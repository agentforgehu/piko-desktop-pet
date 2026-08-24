namespace Piko.Runtime.Ipc;

public sealed record RuntimeMemoryItem(
    string Id,
    string Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string Sensitivity,
    string Summary);

public sealed record RuntimeMemoryListResponse(
    bool Available,
    string Reason,
    IReadOnlyList<RuntimeMemoryItem> Items);
