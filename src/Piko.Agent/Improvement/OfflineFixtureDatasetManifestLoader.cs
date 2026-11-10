using System.Text.Json;
using System.Text.Json.Serialization;

namespace Piko.Agent.Improvement;

/// <summary>Parses an explicit versioned dataset manifest and rejects content drift before evaluation.</summary>
public static class OfflineFixtureDatasetManifestLoader
{
    public const int MaximumManifestBytes = 10 * 1024 * 1024;

    private static readonly JsonSerializerOptions InputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public static OfflineFixtureDatasetManifest Create(
        string datasetId,
        string datasetVersion,
        IReadOnlyList<OfflineFixtureScenario> scenarios)
    {
        var digest = PairedProviderMeasurementRunner.ComputeDatasetSha256(datasetId, datasetVersion, scenarios);
        return new OfflineFixtureDatasetManifest(datasetId, datasetVersion, digest, scenarios);
    }

    public static OfflineFixtureDatasetManifest Parse(ReadOnlySpan<byte> json)
    {
        if (json.Length is 0 or > MaximumManifestBytes)
        {
            throw new InvalidDataException($"Fixture manifest must be between 1 and {MaximumManifestBytes} bytes.");
        }

        using var document = JsonDocument.Parse(json.ToArray());
        EnsureNoDuplicateProperties(document.RootElement);
        var manifest = JsonSerializer.Deserialize<OfflineFixtureDatasetManifest>(json, InputOptions)
            ?? throw new InvalidDataException("Fixture manifest is empty or invalid.");
        if (!IsSha256(manifest.DatasetSha256))
        {
            throw new InvalidDataException("Fixture manifest must include a lowercase SHA-256 digest.");
        }

        var actual = PairedProviderMeasurementRunner.ComputeDatasetSha256(
            manifest.DatasetId, manifest.DatasetVersion, manifest.Scenarios);
        if (!string.Equals(actual, manifest.DatasetSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fixture dataset content does not match its declared SHA-256.");
        }
        return manifest;
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException("Fixture manifests cannot contain duplicate JSON properties.");
                }
                EnsureNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                EnsureNoDuplicateProperties(item);
            }
        }
    }
}
