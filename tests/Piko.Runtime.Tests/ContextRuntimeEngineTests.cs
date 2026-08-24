using Piko.Context.Events;
using Piko.Context.Interventions;
using Piko.Context.Privacy;

namespace Piko.Runtime.Tests;

public sealed class ContextRuntimeEngineTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task DeniedCapability_DoesNotReachSituationEngine()
    {
        using var engine = new ContextRuntimeEngine();
        var contextEvent = Event(
            ContextEventTypes.DiagnosticsChanged,
            ContextCapability.DiagnosticsSummary,
            Start,
            ("errors", ContextDataValue.From(3)));

        var update = await engine.ProcessAsync(contextEvent);

        Assert.False(update.Accepted);
        Assert.Equal("capability_denied", update.Reason);
    }

    [Fact]
    public async Task RepeatedAuthorizedBuildFailures_OfferHelpOnThirdFailure()
    {
        var privacy = PrivacyProfile.LocalFirst()
            .WithGrant(ContextCapability.TerminalSummary, PermissionGrant.AllowSession);
        using var engine = new ContextRuntimeEngine(profile: privacy);
        await engine.ProcessAsync(Event(
            ContextEventTypes.PresenceChanged,
            ContextCapability.Presence,
            Start,
            ("state", new ContextDataValue("active"))));
        await engine.ProcessAsync(Event(
            ContextEventTypes.ForegroundApplicationChanged,
            ContextCapability.ForegroundApplicationCategory,
            Start.AddSeconds(1),
            ("category", new ContextDataValue("development"))));

        ContextRuntimeUpdate? update = null;
        for (var failure = 0; failure < 3; failure++)
        {
            update = await engine.ProcessAsync(Event(
                ContextEventTypes.BuildCompleted,
                ContextCapability.TerminalSummary,
                Start.AddSeconds(2 + failure),
                ("success", ContextDataValue.From(false))));
        }

        Assert.NotNull(update);
        Assert.True(update.Accepted);
        Assert.Equal(3, update.Situation.ConsecutiveBuildFailures);
        Assert.Equal(InterventionKind.OfferHelp, update.Intervention.Kind);
    }

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
