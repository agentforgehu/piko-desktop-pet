namespace Piko.Cognition;

/// <summary>
/// Deterministic functional affect and homeostasis model, independent of any UI, pet,
/// model provider, or tool permission system.
/// </summary>
public sealed class AffectSystem
{
    public AffectSystem(AffectState? initialState = null)
    {
        State = (initialState ?? AffectState.Baseline).Clamp();
    }

    public AffectState State { get; private set; }
    public AffectTransition? LastTransition { get; private set; }

    public AffectTransition Appraise(AppraisalSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        signal = Normalize(signal);
        var before = State;
        var relevance = signal.GoalRelevance;
        var certainty = signal.Certainty;
        var controllability = signal.Controllability;
        var uncertainty = 1 - certainty;
        var outcome = signal.OutcomeValence;
        var arousalDelta = signal.ArousalDelta;

        var stressRise = relevance * (
            Math.Max(0, -outcome) * 0.18 + uncertainty * 0.12 +
            (1 - controllability) * 0.1 + Math.Max(0, arousalDelta) * 0.04);
        var stressRecovery = relevance * Math.Max(0, outcome) * 0.06 +
                             Math.Max(0, signal.SocialSignal) * 0.025;
        var after = new AffectState(
            Math.Clamp(before.Valence + relevance * outcome, -1, 1),
            Math.Clamp(before.Arousal + relevance * arousalDelta +
                       relevance * uncertainty * 0.12 + relevance * (1 - controllability) * 0.06, 0, 1),
            Math.Clamp(before.Energy + signal.EnergyDelta -
                       relevance * Math.Max(0, arousalDelta) * 0.01, 0, 1),
            Math.Clamp(before.SocialContinuity + signal.SocialContinuityDelta, 0, 1),
            Math.Clamp(before.StressLoad + stressRise - stressRecovery, 0, 1),
            Math.Clamp(before.CuriosityDrive + relevance * signal.Novelty * 0.18 -
                       relevance * uncertainty * before.StressLoad * 0.025, 0, 1),
            Math.Clamp(before.RewardExpectation + relevance * signal.GoalProgressDelta * 0.16 +
                       relevance * outcome * 0.04, 0, 1));

        State = after;
        LastTransition = new AffectTransition(signal, before, after);
        return LastTransition;
    }

    /// <summary>Advances bounded recovery. Large clock jumps are capped to avoid runaway transitions.</summary>
    public AffectState Advance(double elapsedSeconds, bool resting = false)
    {
        var dt = double.IsFinite(elapsedSeconds) ? Math.Clamp(elapsedSeconds, 0, 5) : 0;
        var baseline = AffectState.Baseline;
        State = new AffectState(
            Approach(State.Valence, baseline.Valence, 0.08 * dt),
            Approach(State.Arousal, baseline.Arousal, 0.12 * dt),
            Math.Clamp(State.Energy + (resting ? 0.012 : -Math.Max(0, State.StressLoad - 0.2) * 0.001) * dt, 0, 1),
            Approach(State.SocialContinuity, baseline.SocialContinuity, 0.0001 * dt),
            Approach(State.StressLoad, baseline.StressLoad, (resting ? 0.035 : 0.012) * dt),
            Approach(State.CuriosityDrive, baseline.CuriosityDrive, 0.008 * dt),
            Approach(State.RewardExpectation, baseline.RewardExpectation, 0.02 * dt));
        return State;
    }

    private static AppraisalSignal Normalize(AppraisalSignal signal) => signal with
    {
        Cause = string.IsNullOrWhiteSpace(signal.Cause) ? "unspecified" : signal.Cause[..Math.Min(signal.Cause.Length, 64)],
        GoalRelevance = ClampFinite(signal.GoalRelevance, 0, 1),
        OutcomeValence = ClampFinite(signal.OutcomeValence, -0.4, 0.4),
        ArousalDelta = ClampFinite(signal.ArousalDelta, -0.4, 0.4),
        Certainty = ClampFinite(signal.Certainty, 0, 1),
        Controllability = ClampFinite(signal.Controllability, 0, 1),
        EnergyDelta = ClampFinite(signal.EnergyDelta, -0.2, 0.2),
        SocialContinuityDelta = ClampFinite(signal.SocialContinuityDelta, -0.01, 0.01),
        Novelty = ClampFinite(signal.Novelty, 0, 1),
        GoalProgressDelta = ClampFinite(signal.GoalProgressDelta, -1, 1),
        SocialSignal = ClampFinite(signal.SocialSignal, -1, 1)
    };

    private static double ClampFinite(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : 0;

    private static double Approach(double value, double target, double maximumDelta) =>
        value < target
            ? Math.Min(target, value + maximumDelta)
            : Math.Max(target, value - maximumDelta);
}
