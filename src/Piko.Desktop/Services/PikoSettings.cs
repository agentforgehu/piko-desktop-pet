using Piko.Runtime;

namespace Piko.Desktop.Services;

public sealed record PikoSettings
{
    public const int CurrentSchemaVersion = 2;

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
    public bool MemoryEnabled { get; init; }
    public string AiEndpoint { get; init; } = "https://api.openai.com/v1/";
    public string AiModel { get; init; } = "gpt-5.4";
    public bool LastExitWasClean { get; init; } = true;
    public double? SavedFeetX { get; init; }
    public double? SavedFeetY { get; init; }

    public PikoSettings UpgradeOrDefault() => SchemaVersion is 1 or CurrentSchemaVersion
        ? this with { SchemaVersion = CurrentSchemaVersion }
        : new PikoSettings();

    public RuntimeUserSettings ToRuntimeUserSettings() => new()
    {
        DevelopmentAwarenessEnabled = DevelopmentAwarenessEnabled,
        GitAwarenessEnabled = GitAwarenessEnabled,
        AgentReadEnabled = AgentReadEnabled,
        CloudAiEnabled = CloudAiEnabled,
        MemoryEnabled = MemoryEnabled,
        AiEndpoint = AiEndpoint,
        AiModel = AiModel
    };
}
