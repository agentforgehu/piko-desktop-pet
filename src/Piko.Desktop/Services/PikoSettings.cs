namespace Piko.Desktop.Services;

public sealed record PikoSettings
{
    public int SchemaVersion { get; init; } = 1;
    public bool AutonomousBehaviorEnabled { get; init; } = true;
    public bool WindowExplorationEnabled { get; init; } = true;
    public bool PointerAwarenessEnabled { get; init; } = true;
    public bool FileActivityAwarenessEnabled { get; init; } = true;
    public bool ShowMessages { get; init; } = true;
    public bool ClickThrough { get; init; }
    public bool LaunchAtStartup { get; init; }
    public bool LastExitWasClean { get; init; } = true;
    public double? SavedFeetX { get; init; }
    public double? SavedFeetY { get; init; }
}
