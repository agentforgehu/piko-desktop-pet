using Piko.Context.Events;
using Piko.Context.Windows.Observation;

namespace Piko.Runtime;

public sealed class WindowsContextEventSource
{
    private readonly string _sessionId;
    private WindowsContextSnapshot? _previous;

    public WindowsContextEventSource(string sessionId)
    {
        _sessionId = string.IsNullOrWhiteSpace(sessionId)
            ? throw new ArgumentException("Session ID is required.", nameof(sessionId))
            : sessionId;
    }

    public IReadOnlyList<ContextEvent> Diff(WindowsContextSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var events = new List<ContextEvent>();

        if (_previous?.Presence != current.Presence)
        {
            events.Add(ContextEvent.Create(
                ContextEventTypes.PresenceChanged,
                "windows.context",
                current.Timestamp,
                _sessionId,
                ContextCapability.Presence,
                data: new Dictionary<string, ContextDataValue>
                {
                    ["state"] = new(current.Presence.ToString().ToLowerInvariant()),
                    ["idleSeconds"] = ContextDataValue.From(current.IdleSeconds)
                }));
        }

        if (_previous?.ForegroundApplicationCategory != current.ForegroundApplicationCategory)
        {
            events.Add(ContextEvent.Create(
                ContextEventTypes.ForegroundApplicationChanged,
                "windows.context",
                current.Timestamp,
                _sessionId,
                ContextCapability.ForegroundApplicationCategory,
                data: new Dictionary<string, ContextDataValue>
                {
                    ["category"] = new(current.ForegroundApplicationCategory.ToString().ToLowerInvariant())
                }));
        }

        if (_previous?.IsFullscreen != current.IsFullscreen)
        {
            events.Add(ContextEvent.Create(
                ContextEventTypes.FullscreenChanged,
                "windows.context",
                current.Timestamp,
                _sessionId,
                ContextCapability.FullscreenState,
                data: new Dictionary<string, ContextDataValue>
                {
                    ["active"] = ContextDataValue.From(current.IsFullscreen)
                }));
        }

        if (_previous is null ||
            _previous.AvailableMemoryPercent / 5 != current.AvailableMemoryPercent / 5 ||
            _previous.IsOnBattery != current.IsOnBattery ||
            _previous.BatteryPercent / 5 != current.BatteryPercent / 5)
        {
            var data = new Dictionary<string, ContextDataValue>
            {
                ["availableMemoryPercent"] = ContextDataValue.From(current.AvailableMemoryPercent),
                ["onBattery"] = ContextDataValue.From(current.IsOnBattery)
            };
            if (current.BatteryPercent >= 0)
            {
                data["batteryPercent"] = ContextDataValue.From(current.BatteryPercent);
            }

            events.Add(ContextEvent.Create(
                ContextEventTypes.SystemHealthChanged,
                "windows.context",
                current.Timestamp,
                _sessionId,
                ContextCapability.SystemHealth,
                data: data));
        }

        _previous = current;
        return events;
    }
}
