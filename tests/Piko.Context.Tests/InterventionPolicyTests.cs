using Piko.Context.Interventions;
using Piko.Context.Situations;

namespace Piko.Context.Tests;

public sealed class InterventionPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddHours(1);

    [Fact]
    public void ActiveTyping_SuppressesSpokenHelp()
    {
        var policy = new InterventionPolicy();

        var decision = policy.Decide(new InterventionContext(
            State(SituationKind.CodingBlocked, failures: 3, typing: true),
            SituationKind.Coding,
            Now));

        Assert.Equal(InterventionKind.SilentConcern, decision.Kind);
        Assert.False(decision.ShouldSpeak);
    }

    [Fact]
    public void ThirdFailure_WhenIdle_OffersHelpOnce()
    {
        var policy = new InterventionPolicy();
        var context = new InterventionContext(
            State(SituationKind.CodingBlocked, failures: 3),
            SituationKind.Coding,
            Now);

        var first = policy.Decide(context);
        var repeated = policy.Decide(context with { Now = Now.AddMinutes(1) });

        Assert.Equal(InterventionKind.OfferHelp, first.Kind);
        Assert.True(first.ShouldSpeak);
        Assert.Equal(InterventionKind.None, repeated.Kind);
        Assert.Equal("action_cooldown", repeated.Reason);
    }

    [Fact]
    public void DirectRequest_BypassesQuietHoursAndBudgets()
    {
        var policy = new InterventionPolicy(new InterventionPolicyOptions
        {
            ProactiveSpeechLimitPerHour = 0
        });

        var decision = policy.Decide(new InterventionContext(
            State(SituationKind.Meeting, fullscreen: true),
            SituationKind.Meeting,
            Now,
            UserRequestedInteraction: true,
            QuietHours: true));

        Assert.Equal(InterventionKind.RespondToUser, decision.Kind);
        Assert.True(decision.ShouldSpeak);
    }

    [Fact]
    public void RecoveryAfterBlockedSituation_Celebrates()
    {
        var policy = new InterventionPolicy();

        var decision = policy.Decide(new InterventionContext(
            State(SituationKind.Coding),
            SituationKind.CodingBlocked,
            Now));

        Assert.Equal(InterventionKind.Celebrate, decision.Kind);
        Assert.True(decision.ShouldSpeak);
    }

    private static SituationState State(
        SituationKind kind,
        int failures = 0,
        bool typing = false,
        bool fullscreen = false) => new(
        kind,
        Now,
        Now,
        1,
        Array.Empty<string>(),
        failures,
        typing,
        fullscreen);
}
