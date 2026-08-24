using Piko.Context.Events;
using Piko.Context.Situations;
using Piko.Context.Windows.Observation;

namespace Piko.Runtime.Tests;

public sealed class WindowsContextEventSourceTests
{
    [Fact]
    public void Diff_EmitsOnlyChangedPrivacySafeFacts()
    {
        var source = new WindowsContextEventSource("session");
        var first = new WindowsContextSnapshot(
            DateTimeOffset.UnixEpoch,
            PresenceState.Active,
            0,
            ApplicationCategory.Development,
            false);
        var unchanged = first with { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(1), IdleSeconds = 1 };
        var changed = unchanged with
        {
            Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(2),
            Presence = PresenceState.Idle,
            IdleSeconds = 120,
            IsFullscreen = true
        };

        var initialEvents = source.Diff(first);
        var noEvents = source.Diff(unchanged);
        var changedEvents = source.Diff(changed);

        Assert.Equal(4, initialEvents.Count);
        Assert.Empty(noEvents);
        Assert.Equal(2, changedEvents.Count);
        Assert.Contains(changedEvents, item => item.Type == ContextEventTypes.PresenceChanged);
        Assert.Contains(changedEvents, item => item.Type == ContextEventTypes.FullscreenChanged);
        Assert.DoesNotContain(initialEvents.SelectMany(item => item.Data.Keys), key =>
            key.Contains("title", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("process", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(initialEvents, item => item.Type == ContextEventTypes.SystemHealthChanged);
    }

    [Fact]
    public void Diff_BucketsSystemHealthToAvoidNoisyEvents()
    {
        var source = new WindowsContextEventSource("session");
        var first = new WindowsContextSnapshot(
            DateTimeOffset.UnixEpoch,
            PresenceState.Active,
            0,
            ApplicationCategory.General,
            false,
            64,
            true,
            52);
        var sameBuckets = first with
        {
            Timestamp = first.Timestamp.AddSeconds(1),
            AvailableMemoryPercent = 62,
            BatteryPercent = 50
        };
        var changedBucket = sameBuckets with
        {
            Timestamp = first.Timestamp.AddSeconds(2),
            AvailableMemoryPercent = 59
        };

        source.Diff(first);
        Assert.Empty(source.Diff(sameBuckets));
        var events = source.Diff(changedBucket);

        var health = Assert.Single(events);
        Assert.Equal(ContextEventTypes.SystemHealthChanged, health.Type);
        Assert.Equal(ContextCapability.SystemHealth, health.Capability);
    }
}
