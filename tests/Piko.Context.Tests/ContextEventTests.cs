using Piko.Context.Events;

namespace Piko.Context.Tests;

public sealed class ContextEventTests
{
    [Fact]
    public void Create_RejectsInvalidConfidence()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContextEvent.Create(
            ContextEventTypes.PresenceChanged,
            "test",
            DateTimeOffset.UnixEpoch,
            "session",
            ContextCapability.Presence,
            confidence: 1.1));
    }

    [Fact]
    public void TypedFields_ParseInvariantValues()
    {
        var contextEvent = ContextEvent.Create(
            ContextEventTypes.BuildCompleted,
            "test",
            DateTimeOffset.UnixEpoch,
            "session",
            ContextCapability.TerminalSummary,
            data: new Dictionary<string, ContextDataValue>
            {
                ["success"] = ContextDataValue.From(false),
                ["errors"] = ContextDataValue.From(3)
            });

        Assert.True(contextEvent.TryGetBoolean("success", out var success));
        Assert.False(success);
        Assert.True(contextEvent.TryGetInt32("errors", out var errors));
        Assert.Equal(3, errors);
    }
}
