using Piko.Context.Events;
using Piko.Context.Situations;

namespace Piko.Context.Tests;

public sealed class SituationEngineTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Presence_TransitionsFromAwayToReturnedThenActive()
    {
        var engine = new SituationEngine(new SituationEngineOptions
        {
            ReturnedDuration = TimeSpan.FromSeconds(10)
        });

        Assert.Equal(SituationKind.Away, engine.Observe(Presence("idle", Start)).Kind);
        Assert.Equal(SituationKind.Returned, engine.Observe(Presence("active", Start.AddSeconds(5))).Kind);
        Assert.Equal(SituationKind.Active, engine.Evaluate(Start.AddSeconds(16)).Kind);
    }

    [Fact]
    public void DevelopmentBuilds_ProduceBuildingAndBlockedSituations()
    {
        var engine = new SituationEngine();
        engine.Observe(Presence("active", Start));
        engine.Observe(App("development", Start.AddSeconds(1)));

        Assert.Equal(SituationKind.Building, engine.Observe(Event(
            ContextEventTypes.BuildStarted,
            ContextCapability.TerminalSummary,
            Start.AddSeconds(2))).Kind);

        var failed = engine.Observe(Event(
            ContextEventTypes.BuildCompleted,
            ContextCapability.TerminalSummary,
            Start.AddSeconds(3),
            ("success", ContextDataValue.From(false))));

        Assert.Equal(SituationKind.CodingBlocked, failed.Kind);
        Assert.Equal(1, failed.ConsecutiveBuildFailures);
        Assert.Contains("build:failed:1", failed.Evidence);
    }

    [Fact]
    public void OldEventsBeyondTolerance_AreIgnored()
    {
        var engine = new SituationEngine(new SituationEngineOptions
        {
            MaximumOutOfOrderAge = TimeSpan.FromSeconds(2)
        });
        engine.Observe(Presence("active", Start.AddSeconds(10)));

        var state = engine.Observe(Presence("locked", Start));

        Assert.Equal(SituationKind.Active, state.Kind);
    }

    [Fact]
    public void FullscreenCommunication_EntersMeeting()
    {
        var engine = new SituationEngine();
        engine.Observe(Presence("active", Start));
        engine.Observe(App("communication", Start.AddSeconds(1)));
        var state = engine.Observe(Event(
            ContextEventTypes.FullscreenChanged,
            ContextCapability.FullscreenState,
            Start.AddSeconds(2),
            ("active", ContextDataValue.From(true))));

        Assert.Equal(SituationKind.Meeting, state.Kind);
        Assert.True(state.IsFullscreen);
    }

    [Fact]
    public void FailedTestSummary_EntersCodingBlockedWithoutReadingTestOutput()
    {
        var engine = new SituationEngine();
        engine.Observe(Presence("active", Start));
        engine.Observe(App("development", Start.AddSeconds(1)));

        var state = engine.Observe(Event(
            ContextEventTypes.TestsCompleted,
            ContextCapability.DevelopmentActivity,
            Start.AddSeconds(2),
            ("failed", ContextDataValue.From(2))));

        Assert.Equal(SituationKind.CodingBlocked, state.Kind);
        Assert.Contains("tests:failed:2", state.Evidence);
    }

    private static ContextEvent Presence(string state, DateTimeOffset timestamp) => Event(
        ContextEventTypes.PresenceChanged,
        ContextCapability.Presence,
        timestamp,
        ("state", new ContextDataValue(state)));

    private static ContextEvent App(string category, DateTimeOffset timestamp) => Event(
        ContextEventTypes.ForegroundApplicationChanged,
        ContextCapability.ForegroundApplicationCategory,
        timestamp,
        ("category", new ContextDataValue(category)));

    private static ContextEvent Event(
        string type,
        ContextCapability capability,
        DateTimeOffset timestamp,
        params (string Key, ContextDataValue Value)[] fields) => ContextEvent.Create(
        type,
        "test",
        timestamp,
        "session",
        capability,
        data: fields.ToDictionary(item => item.Key, item => item.Value));
}
