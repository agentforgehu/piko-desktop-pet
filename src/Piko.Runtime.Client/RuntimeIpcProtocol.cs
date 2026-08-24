using System.Text.Json;

namespace Piko.Runtime.Ipc;

public sealed record RuntimeRequest(
    int SchemaVersion,
    string RequestId,
    string Type,
    JsonElement? Payload = null)
{
    public const int CurrentSchemaVersion = 1;

    public static RuntimeRequest Create(string type) => new(
        CurrentSchemaVersion,
        Guid.NewGuid().ToString("N"),
        type);
}

public sealed record RuntimeResponse(
    int SchemaVersion,
    string RequestId,
    bool Success,
    string Type,
    string? Error,
    JsonElement? Payload)
{
    public const int CurrentSchemaVersion = 1;

    public static RuntimeResponse Ok<T>(string requestId, string type, T payload) => new(
        CurrentSchemaVersion,
        requestId,
        true,
        type,
        null,
        JsonSerializer.SerializeToElement(payload));

    public static RuntimeResponse Fail(string requestId, string error) => new(
        CurrentSchemaVersion,
        requestId,
        false,
        "error",
        error,
        null);
}
