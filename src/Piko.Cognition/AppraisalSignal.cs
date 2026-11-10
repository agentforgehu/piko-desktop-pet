namespace Piko.Cognition;

/// <summary>Structured, bounded evidence used to update an agent's functional affect state.</summary>
public sealed record AppraisalSignal(
    string Cause,
    double GoalRelevance,
    double OutcomeValence,
    double ArousalDelta,
    double Certainty,
    double Controllability,
    double EnergyDelta = 0,
    double SocialContinuityDelta = 0,
    double Novelty = 0,
    double GoalProgressDelta = 0,
    double SocialSignal = 0);

public sealed record AffectTransition(
    AppraisalSignal Signal,
    AffectState Before,
    AffectState After);
