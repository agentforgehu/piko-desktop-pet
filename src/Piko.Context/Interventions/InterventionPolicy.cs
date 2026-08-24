using Piko.Context.Situations;

namespace Piko.Context.Interventions;

public sealed class InterventionPolicy
{
    private readonly InterventionPolicyOptions _options;
    private readonly Queue<DateTimeOffset> _speechHistory = new();
    private readonly Dictionary<string, DateTimeOffset> _lastActions = new(StringComparer.Ordinal);

    public InterventionPolicy(InterventionPolicyOptions? options = null)
    {
        _options = options ?? new InterventionPolicyOptions();
    }

    public InterventionDecision Decide(InterventionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.UserRequestedInteraction)
        {
            return new InterventionDecision(
                InterventionKind.RespondToUser,
                "user.respond",
                true,
                "direct_user_request");
        }

        if (context.Situation.Kind == SituationKind.Away)
        {
            return InterventionDecision.None("user_away");
        }

        if (context.QuietHours)
        {
            return InterventionDecision.None("quiet_hours");
        }

        if (context.Situation.Kind is SituationKind.Meeting or SituationKind.Gaming ||
            context.Situation.IsFullscreen)
        {
            return InterventionDecision.None("do_not_disturb_context");
        }

        if (context.Situation.UserIsActivelyTyping)
        {
            return context.Situation.Kind == SituationKind.CodingBlocked
                ? new InterventionDecision(
                    InterventionKind.SilentConcern,
                    "pet.concern.silent",
                    false,
                    "user_actively_typing")
                : InterventionDecision.None("user_actively_typing");
        }

        if (context.Situation.Kind == SituationKind.CodingBlocked)
        {
            if (context.Situation.ConsecutiveBuildFailures < _options.BuildFailuresBeforeOffer)
            {
                return new InterventionDecision(
                    InterventionKind.SilentConcern,
                    "pet.concern.silent",
                    false,
                    "waiting_for_repeated_failure");
            }

            return TryProactive(
                context.Now,
                "development.offer-help",
                InterventionKind.OfferHelp,
                "development.offer-help",
                "repeated_build_failure");
        }

        if (context.PreviousSituation == SituationKind.CodingBlocked &&
            context.Situation.Kind == SituationKind.Coding)
        {
            return TryProactive(
                context.Now,
                "development.recovered",
                InterventionKind.Celebrate,
                "development.celebrate",
                "build_recovered");
        }

        if (context.Situation.Kind == SituationKind.Returned)
        {
            return TryProactive(
                context.Now,
                "presence.returned",
                InterventionKind.Greet,
                "presence.greet",
                "user_returned");
        }

        return InterventionDecision.None("no_intervention_needed");
    }

    private InterventionDecision TryProactive(
        DateTimeOffset now,
        string key,
        InterventionKind kind,
        string semanticAction,
        string reason)
    {
        while (_speechHistory.TryPeek(out var timestamp) && now - timestamp >= TimeSpan.FromHours(1))
        {
            _speechHistory.Dequeue();
        }

        if (_speechHistory.Count >= _options.ProactiveSpeechLimitPerHour)
        {
            return InterventionDecision.None("hourly_speech_budget_exhausted");
        }

        if (_lastActions.TryGetValue(key, out var previous) && now - previous < _options.SameActionCooldown)
        {
            return InterventionDecision.None("action_cooldown");
        }

        _speechHistory.Enqueue(now);
        _lastActions[key] = now;
        return new InterventionDecision(kind, semanticAction, true, reason);
    }
}
