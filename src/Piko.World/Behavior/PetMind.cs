namespace Piko.World.Behavior;

public enum PetStimulus
{
    SilentConcern,
    Greet,
    OfferHelp,
    Celebrate,
    RespondToUser
}

public readonly record struct PetEmotionState(
    double Valence,
    double Arousal,
    double Energy,
    double Attachment)
{
    public static PetEmotionState Baseline => new(0.18, 0.28, 0.72, 0.55);
}

public sealed record PetAppraisalEvent(
    string Trigger,
    double GoalRelevance,
    double OutcomeValence,
    double ArousalDelta,
    double Certainty,
    double Controllability,
    double EnergyDelta = 0,
    double RelationshipDelta = 0);

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
        Warmth = Math.Clamp(Warmth, 0, 1),
        Playfulness = Math.Clamp(Playfulness, 0, 1),
        Restraint = Math.Clamp(Restraint, 0, 1)
    };
}

public sealed record PetReaction(
    PetCommand Command,
    string Message,
    bool ShouldSpeak,
    PetEmotionState Emotion);

public sealed class PetMind
{
    private readonly PetPersonality _personality;

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

    public PetEmotionState Appraise(PetAppraisalEvent appraisal)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        appraisal = appraisal with
        {
            Trigger = string.IsNullOrWhiteSpace(appraisal.Trigger)
                ? "unspecified"
                : appraisal.Trigger[..Math.Min(appraisal.Trigger.Length, 64)]
        };
        var relevance = ClampFinite(appraisal.GoalRelevance, 0, 1);
        var certainty = ClampFinite(appraisal.Certainty, 0, 1);
        var controllability = ClampFinite(appraisal.Controllability, 0, 1);
        var before = Emotion;
        var after = new PetEmotionState(
            Math.Clamp(before.Valence + relevance * ClampFinite(appraisal.OutcomeValence, -0.4, 0.4), -1, 1),
            Math.Clamp(before.Arousal + relevance * ClampFinite(appraisal.ArousalDelta, -0.4, 0.4) +
                       relevance * (1 - certainty) * 0.12 + relevance * (1 - controllability) * 0.06, 0, 1),
            Math.Clamp(before.Energy + ClampFinite(appraisal.EnergyDelta, -0.2, 0.2), 0, 1),
            Math.Clamp(before.Attachment + ClampFinite(appraisal.RelationshipDelta, -0.01, 0.01), 0, 1));
        Emotion = after;
        LastTransition = new PetEmotionTransition(appraisal, before, after);
        return after;
    }

    public PetEmotionState Advance(double elapsedSeconds)
    {
        var dt = Math.Clamp(elapsedSeconds, 0, 1);
        var baseline = PetEmotionState.Baseline;
        Emotion = new PetEmotionState(
            Approach(Emotion.Valence, baseline.Valence, 0.08 * dt),
            Approach(Emotion.Arousal, baseline.Arousal, 0.12 * dt),
            Approach(Emotion.Energy, baseline.Energy, 0.035 * dt),
            Approach(Emotion.Attachment, baseline.Attachment, 0.008 * dt));
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
                appraisalTrigger: "silent_concern"),
            PetStimulus.OfferHelp => Create(
                PetCommand.Concern,
                "看起来卡住了，要我帮你看看吗？",
                policyAllowsSpeech,
                valence: -0.08,
                arousal: 0.2 * (1 - restraint / 2),
                energy: -0.025,
                attachment: 0.003 * warmth,
                appraisalTrigger: "offer_help"),
            PetStimulus.Greet => Create(
                PetCommand.Greet,
                "你回来啦，我一直在这里",
                policyAllowsSpeech,
                valence: 0.2 * warmth,
                arousal: 0.14 * playfulness,
                energy: 0.035,
                attachment: 0.003 * warmth,
                appraisalTrigger: "greeting"),
            PetStimulus.Celebrate => Create(
                PetCommand.Celebrate,
                "成功啦！做得真棒",
                policyAllowsSpeech,
                valence: 0.32 * (0.5 + playfulness / 2),
                arousal: 0.3 * playfulness,
                energy: -0.015,
                attachment: 0.004 * warmth,
                appraisalTrigger: "celebration"),
            PetStimulus.RespondToUser => Create(
                PetCommand.Greet,
                "我在，正在听你说",
                policyAllowsSpeech,
                valence: 0.1 * warmth,
                arousal: 0.08,
                energy: 0,
                attachment: 0.002 * warmth,
                appraisalTrigger: "user_message"),
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
        var (valence, arousal, energy) = emotion switch
        {
            "happy" => (0.18, 0.12, 0.02),
            "excited" => (0.26, 0.26, 0.04),
            "concerned" => (-0.12, 0.10, -0.02),
            "calm" => (0.08, -0.10, -0.01),
            _ => (0.04, 0.02, 0.0)
        };

        var reaction = Create(
            command,
            safeMessage,
            policyAllowsSpeech,
            valence,
            arousal,
            energy,
            0,
            "model_expression");
        Emotion = reaction.Emotion;
        return reaction;
    }

    private PetReaction Create(
        PetCommand command,
        string message,
        bool shouldSpeak,
        double valence,
        double arousal,
        double energy,
        double attachment,
        string appraisalTrigger = "interaction")
    {
        var emotion = Appraise(new PetAppraisalEvent(
            appraisalTrigger,
            GoalRelevance: 1,
            OutcomeValence: valence,
            ArousalDelta: arousal,
            Certainty: 0.9,
            Controllability: 0.8,
            EnergyDelta: energy,
            RelationshipDelta: attachment));
        return new PetReaction(command, message, shouldSpeak, emotion);
    }

    private static double ClampFinite(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : 0;

    private static double Approach(double value, double target, double maximumDelta) =>
        value < target
            ? Math.Min(target, value + maximumDelta)
            : Math.Max(target, value - maximumDelta);
}

