namespace Piko.World.Behavior;

public enum PetStimulus
{
    SilentConcern,
    Greet,
    OfferHelp,
    Celebrate,
    RespondToUser
}

public record struct PetEmotionState(
    double Valence,
    double Arousal,
    double Energy,
    double Attachment)
{
    public double StressLoad { get; init; }
    public double CuriosityDrive { get; init; }
    public double RewardExpectation { get; init; }

    public static PetEmotionState Baseline => new(0.18, 0.28, 0.72, 0.55)
    {
        StressLoad = 0.12,
        CuriosityDrive = 0.45,
        RewardExpectation = 0.5
    };
}

public sealed record PetAppraisalEvent(
    string Trigger,
    double GoalRelevance,
    double OutcomeValence,
    double ArousalDelta,
    double Certainty,
    double Controllability,
    double EnergyDelta = 0,
    double RelationshipDelta = 0,
    double Novelty = 0,
    double GoalProgressDelta = 0,
    double SocialSignal = 0);

public sealed record PetEmotionTransition(
    PetAppraisalEvent Appraisal,
    PetEmotionState Before,
    PetEmotionState After);

public sealed record PetPersonality(
    double Warmth = 0.82,
    double Playfulness = 0.68,
    double Restraint = 0.78)
{
    public PetPersonality Validate() => this with
    {
        Warmth = ClampPersonality(Warmth),
        Playfulness = ClampPersonality(Playfulness),
        Restraint = ClampPersonality(Restraint)
    };

    private static double ClampPersonality(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5;
}

public sealed record PetReaction(
    PetCommand Command,
    string Message,
    bool ShouldSpeak,
    PetEmotionState Emotion);

public sealed class PetMind
{
    private PetPersonality _personality;

    public PetMind(PetPersonality? personality = null)
    {
        _personality = (personality ?? new PetPersonality()).Validate();
        Emotion = PetEmotionState.Baseline;
    }

    public PetEmotionState Emotion { get; private set; }

    /// <summary>
    /// The most recent explainable simulated state transition. This is interaction state,
    /// not a claim of subjective feeling and never changes tool permissions.
    /// </summary>
    public PetEmotionTransition? LastTransition { get; private set; }

    public void SetPersonality(PetPersonality personality) =>
        _personality = (personality ?? throw new ArgumentNullException(nameof(personality))).Validate();

    public PetEmotionState Appraise(PetAppraisalEvent appraisal)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        appraisal = appraisal with
        {
            Trigger = string.IsNullOrWhiteSpace(appraisal.Trigger)
                ? "unspecified"
                : appraisal.Trigger[..Math.Min(appraisal.Trigger.Length, 64)],
            GoalRelevance = ClampFinite(appraisal.GoalRelevance, 0, 1),
            OutcomeValence = ClampFinite(appraisal.OutcomeValence, -0.4, 0.4),
            ArousalDelta = ClampFinite(appraisal.ArousalDelta, -0.4, 0.4),
            Certainty = ClampFinite(appraisal.Certainty, 0, 1),
            Controllability = ClampFinite(appraisal.Controllability, 0, 1),
            EnergyDelta = ClampFinite(appraisal.EnergyDelta, -0.2, 0.2),
            RelationshipDelta = ClampFinite(appraisal.RelationshipDelta, -0.01, 0.01),
            Novelty = ClampFinite(appraisal.Novelty, 0, 1),
            GoalProgressDelta = ClampFinite(appraisal.GoalProgressDelta, -1, 1),
            SocialSignal = ClampFinite(appraisal.SocialSignal, -1, 1)
        };
        var relevance = ClampFinite(appraisal.GoalRelevance, 0, 1);
        var certainty = ClampFinite(appraisal.Certainty, 0, 1);
        var controllability = ClampFinite(appraisal.Controllability, 0, 1);
        var outcome = ClampFinite(appraisal.OutcomeValence, -0.4, 0.4);
        var arousalDelta = ClampFinite(appraisal.ArousalDelta, -0.4, 0.4);
        var novelty = ClampFinite(appraisal.Novelty, 0, 1);
        var progress = ClampFinite(appraisal.GoalProgressDelta, -1, 1);
        var socialSignal = ClampFinite(appraisal.SocialSignal, -1, 1);
        var before = Emotion;
        var uncertainty = 1 - certainty;
        var stressRise = relevance * (
            Math.Max(0, -outcome) * 0.18 + uncertainty * 0.12 +
            (1 - controllability) * 0.1 + Math.Max(0, arousalDelta) * 0.04);
        var stressRecovery = relevance * Math.Max(0, outcome) * 0.06 +
                             Math.Max(0, socialSignal) * 0.025;
        var after = new PetEmotionState(
            Math.Clamp(before.Valence + relevance * outcome, -1, 1),
            Math.Clamp(before.Arousal + relevance * arousalDelta +
                       relevance * uncertainty * 0.12 + relevance * (1 - controllability) * 0.06, 0, 1),
            Math.Clamp(before.Energy + ClampFinite(appraisal.EnergyDelta, -0.2, 0.2) -
                       relevance * Math.Max(0, arousalDelta) * 0.01, 0, 1),
            Math.Clamp(before.Attachment + ClampFinite(appraisal.RelationshipDelta, -0.01, 0.01), 0, 1))
        {
            StressLoad = Math.Clamp(before.StressLoad + stressRise - stressRecovery, 0, 1),
            CuriosityDrive = Math.Clamp(before.CuriosityDrive + relevance * novelty * 0.18 -
                                        relevance * uncertainty * before.StressLoad * 0.025, 0, 1),
            RewardExpectation = Math.Clamp(before.RewardExpectation + relevance * progress * 0.16 +
                                           relevance * outcome * 0.04, 0, 1)
        };
        Emotion = after;
        LastTransition = new PetEmotionTransition(appraisal, before, after);
        return after;
    }

    public PetEmotionState Advance(double elapsedSeconds, bool resting = false)
    {
        var dt = ClampFinite(elapsedSeconds, 0, 1);
        var baseline = PetEmotionState.Baseline;
        Emotion = new PetEmotionState(
            Approach(Emotion.Valence, baseline.Valence, 0.08 * dt),
            Approach(Emotion.Arousal, baseline.Arousal, 0.12 * dt),
            Math.Clamp(Emotion.Energy + (resting ? 0.012 : -Math.Max(0, Emotion.StressLoad - 0.2) * 0.001) * dt, 0, 1),
            Approach(Emotion.Attachment, baseline.Attachment, 0.0001 * dt))
        {
            StressLoad = Approach(Emotion.StressLoad, baseline.StressLoad, (resting ? 0.035 : 0.012) * dt),
            CuriosityDrive = Approach(Emotion.CuriosityDrive, baseline.CuriosityDrive, 0.008 * dt),
            RewardExpectation = Approach(Emotion.RewardExpectation, baseline.RewardExpectation, 0.02 * dt)
        };
        return Emotion;
    }

    public PetReaction React(PetStimulus stimulus, bool policyAllowsSpeech)
    {
        var warmth = _personality.Warmth;
        var playfulness = _personality.Playfulness;
        var restraint = _personality.Restraint;

        var reaction = stimulus switch
        {
            PetStimulus.SilentConcern => Create(
                PetCommand.Concern,
                "我先安静陪着你",
                false,
                valence: -0.22 * warmth,
                arousal: 0.12,
                energy: -0.04,
                attachment: 0.002 * warmth,
                appraisalTrigger: "silent_concern",
                certainty: 0.65,
                controllability: 0.2),
            PetStimulus.OfferHelp => Create(
                PetCommand.Concern,
                "看起来卡住了，要我帮你看看吗？",
                policyAllowsSpeech,
                valence: -0.08,
                arousal: 0.2 * (1 - restraint / 2),
                energy: -0.025,
                attachment: 0.003 * warmth,
                appraisalTrigger: "offer_help",
                certainty: 0.65,
                controllability: 0.55,
                novelty: 0.08),
            PetStimulus.Greet => Create(
                PetCommand.Greet,
                "你回来啦，我一直在这里",
                policyAllowsSpeech,
                valence: 0.2 * warmth,
                arousal: 0.14 * playfulness,
                energy: 0.035,
                attachment: 0.003 * warmth,
                appraisalTrigger: "greeting",
                certainty: 0.9,
                controllability: 0.9,
                novelty: 0.15,
                socialSignal: 0.2),
            PetStimulus.Celebrate => Create(
                PetCommand.Celebrate,
                "成功啦！做得真棒",
                policyAllowsSpeech,
                valence: 0.32 * (0.5 + playfulness / 2),
                arousal: 0.3 * playfulness,
                energy: -0.015,
                attachment: 0.004 * warmth,
                appraisalTrigger: "celebration",
                certainty: 0.95,
                controllability: 0.9,
                novelty: 0.1,
                goalProgressDelta: 0.8,
                socialSignal: 0.3),
            PetStimulus.RespondToUser => Create(
                PetCommand.Greet,
                "我在，正在听你说",
                policyAllowsSpeech,
                valence: 0.1 * warmth,
                arousal: 0.08,
                energy: 0,
                attachment: 0.002 * warmth,
                appraisalTrigger: "user_message",
                certainty: 0.85,
                controllability: 0.8,
                novelty: 0.04,
                socialSignal: 0.12),
            _ => throw new ArgumentOutOfRangeException(nameof(stimulus))
        };

        Emotion = reaction.Emotion;
        return reaction;
    }

    public PetReaction ReactToModel(string message, string emotion, string action, bool policyAllowsSpeech = true)
    {
        var safeMessage = string.IsNullOrWhiteSpace(message)
            ? "我在，正在听你说"
            : message.Trim();
        if (safeMessage.Length > 500)
        {
            safeMessage = safeMessage[..500];
        }

        var command = action switch
        {
            "celebrate" => PetCommand.Celebrate,
            "concern" => PetCommand.Concern,
            "rest" => PetCommand.Rest,
            _ => PetCommand.Greet
        };
        // Generated wording and labels are presentation proposals, not evidence about
        // the pet's internal state. Only evaluated external events may update PetMind.
        _ = emotion;
        return new PetReaction(command, safeMessage, policyAllowsSpeech, Emotion);
    }

    private PetReaction Create(
        PetCommand command,
        string message,
        bool shouldSpeak,
        double valence,
        double arousal,
        double energy,
        double attachment,
        string appraisalTrigger = "interaction",
        double certainty = 0.9,
        double controllability = 0.8,
        double novelty = 0,
        double goalProgressDelta = 0,
        double socialSignal = 0)
    {
        var emotion = Appraise(new PetAppraisalEvent(
            appraisalTrigger,
            GoalRelevance: 1,
            OutcomeValence: valence,
            ArousalDelta: arousal,
            Certainty: certainty,
            Controllability: controllability,
            EnergyDelta: energy,
            RelationshipDelta: attachment,
            Novelty: novelty,
            GoalProgressDelta: goalProgressDelta,
            SocialSignal: socialSignal));
        return new PetReaction(command, message, shouldSpeak, emotion);
    }

    private static double ClampFinite(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : 0;

    private static double Approach(double value, double target, double maximumDelta) =>
        value < target
            ? Math.Min(target, value + maximumDelta)
            : Math.Max(target, value - maximumDelta);
}

