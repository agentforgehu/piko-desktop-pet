using Piko.Context.Events;

namespace Piko.Context.Tests;

public sealed class ContextEventBusTests
{
    [Fact]
    public async Task Publish_IsOrderedAndIsolatesHandlerFailures()
    {
        using var bus = new ContextEventBus();
        var calls = new List<int>();
        using var first = bus.Subscribe((_, _) =>
        {
            calls.Add(1);
            return ValueTask.CompletedTask;
        });
        using var broken = bus.Subscribe((_, _) => throw new InvalidOperationException("test"));
        using var third = bus.Subscribe((_, _) =>
        {
            calls.Add(3);
            return ValueTask.CompletedTask;
        });

        var receipt = await bus.PublishAsync(Event(ContextEventTypes.PresenceChanged));

        Assert.Equal(new[] { 1, 3 }, calls);
        Assert.Equal(3, receipt.HandlerCount);
        Assert.Equal(2, receipt.SuccessfulHandlers);
        Assert.Single(receipt.Failures);
    }

    [Fact]
    public async Task DisposedSubscription_DoesNotReceiveEvents()
    {
        using var bus = new ContextEventBus();
        var calls = 0;
        var subscription = bus.Subscribe((_, _) =>
        {
            calls++;
            return ValueTask.CompletedTask;
        });
        subscription.Dispose();

        var receipt = await bus.PublishAsync(Event(ContextEventTypes.PresenceChanged));

        Assert.Equal(0, calls);
        Assert.Equal(0, receipt.HandlerCount);
    }

    private static ContextEvent Event(string type) => ContextEvent.Create(
        type,
        "test",
        DateTimeOffset.UnixEpoch,
        "session",
        ContextCapability.Presence);
}
