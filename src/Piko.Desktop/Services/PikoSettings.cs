using Piko.Runtime;

namespace Piko.Desktop.Services;

public sealed record PikoSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public bool AutonomousBehaviorEnabled { get; init; } = true;
    public bool WindowExplorationEnabled { get; init; } = true;
    public bool PointerAwarenessEnabled { get; init; } = true;
    public bool FileActivityAwarenessEnabled { get; init; } = true;
    public bool ShowMessages { get; init; } = true;
    public bool ClickThrough { get; init; }
    public bool LaunchAtStartup { get; init; }
    public bool DevelopmentAwarenessEnabled { get; init; }
    public bool GitAwarenessEnabled { get; init; }
    public bool AgentReadEnabled { get; init; }
    public bool CloudAiEnabled { get; init; }
    public AiProviderMode ProviderMode { get; init; } = AiProviderMode.Disabled;
    public bool MemoryEnabled { get; init; }
    public string AiEndpoint { get; init; } = "https://api.openai.com/v1/";
    public string AiModel { get; init; } = "gpt-5.4";
    public string LocalAiEndpoint { get; init; } = "http://127.0.0.1:11434/v1/";
    public string LocalAiModel { get; init; } = "local-model";
    public UserAddressMode UserAddressMode { get; init; } = UserAddressMode.Master;
    public string UserName { get; init; } = string.Empty;
    public string CustomAddress { get; init; } = string.Empty;
    public string Personality { get; init; } = "温暖、简短、稍微活泼";
    public PetProactivity Proactivity { get; init; } = PetProactivity.Low;
    public bool ForegroundActivityAwarenessEnabled { get; init; } = true;
    public bool LastExitWasClean { get; init; } = true;
    public double? SavedFeetX { get; init; }
    public double? SavedFeetY { get; init; }

    public PikoSettings UpgradeOrDefault() => SchemaVersion is 1 or 2 or CurrentSchemaVersion
        ? this with
        {
            SchemaVersion = CurrentSchemaVersion,
            ProviderMode = SchemaVersion < CurrentSchemaVersion && CloudAiEnabled
                ? AiProviderMode.OpenAiApi
                : ProviderMode
        }
        : new PikoSettings();

    public RuntimeUserSettings ToRuntimeUserSettings() => new()
    {
        DevelopmentAwarenessEnabled = DevelopmentAwarenessEnabled,
        GitAwarenessEnabled = GitAwarenessEnabled,
        AgentReadEnabled = AgentReadEnabled,
        CloudAiEnabled = CloudAiEnabled,
        ProviderMode = ProviderMode,
        MemoryEnabled = MemoryEnabled,
        AiEndpoint = AiEndpoint,
        AiModel = AiModel,
        LocalAiEndpoint = LocalAiEndpoint,
        LocalAiModel = LocalAiModel,
        UserAddressMode = UserAddressMode,
        UserName = UserName,
        CustomAddress = CustomAddress,
        Personality = Personality,
        Proactivity = Proactivity,
        ForegroundActivityAwarenessEnabled = ForegroundActivityAwarenessEnabled,
        FileActivityAwarenessEnabled = FileActivityAwarenessEnabled
    };
}

