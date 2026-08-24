namespace Piko.Context.Events;

public static class ContextEventTypes
{
    public const string PresenceChanged = "presence.changed";
    public const string ForegroundApplicationChanged = "application.foreground.changed";
    public const string FullscreenChanged = "display.fullscreen.changed";
    public const string InputIntensityChanged = "input.intensity.changed";
    public const string FileActivityChanged = "file.activity.changed";
    public const string SystemHealthChanged = "system.health.changed";
    public const string BuildStarted = "development.build.started";
    public const string BuildCompleted = "development.build.completed";
    public const string TestsCompleted = "development.tests.completed";
    public const string DiagnosticsChanged = "development.diagnostics.changed";
    public const string GitActivity = "development.git.activity";
    public const string MediaPlaybackChanged = "media.playback.changed";
}
