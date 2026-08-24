using Piko.Context.Situations;

namespace Piko.Context.Windows.Observation;

public sealed record WindowsContextSnapshot(
    DateTimeOffset Timestamp,
    PresenceState Presence,
    int IdleSeconds,
    ApplicationCategory ForegroundApplicationCategory,
    bool IsFullscreen,
    int AvailableMemoryPercent = 100,
    bool IsOnBattery = false,
    int BatteryPercent = -1);
