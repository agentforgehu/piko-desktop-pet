using System.Text.Json;
using System.Text.Json.Serialization;

namespace Piko.Desktop.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;

    public SettingsStore(AppPaths paths)
    {
        _paths = paths;
    }

    public PikoSettings Load()
    {
        try
        {
            var settings = File.Exists(_paths.SettingsFile)
                ? JsonSerializer.Deserialize<PikoSettings>(File.ReadAllText(_paths.SettingsFile), JsonOptions)
                  ?? new PikoSettings()
                : new PikoSettings();
            return settings.UpgradeOrDefault();
        }
        catch
        {
            return new PikoSettings();
        }
    }

    public void Save(PikoSettings settings)
    {
        var temporary = _paths.SettingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, _paths.SettingsFile, true);
    }
}

