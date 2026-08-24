using System.Text.Json;
using System.Text.Json.Serialization;
using Piko.World.Model;

namespace Piko.World.Serialization;

public static class DesktopSnapshotJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(DesktopSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);

    public static DesktopSnapshot Deserialize(string json) =>
        JsonSerializer.Deserialize<DesktopSnapshot>(json, Options)
        ?? throw new JsonException("Snapshot JSON produced no value.");
}
