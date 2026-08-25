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
                attachment: 0.025 * warmth),
            PetStimulus.OfferHelp => Create(
                PetCommand.Concern,
                "看起来卡住了，要我帮你看看吗？",
                policyAllowsSpeech,
                valence: -0.08,
                arousal: 0.2 * (1 - restraint / 2),
                energy: -0.025,
                attachment: 0.04 * warmth),
            PetStimulus.Greet => Create(
                PetCommand.Greet,
                "你回来啦，我一直在这里",
                policyAllowsSpeech,
                valence: 0.2 * warmth,
                arousal: 0.14 * playfulness,
                energy: 0.035,
                attachment: 0.045 * warmth),
            PetStimulus.Celebrate => Create(
                PetCommand.Celebrate,
                "成功啦！做得真棒",
                policyAllowsSpeech,
                valence: 0.32 * (0.5 + playfulness / 2),
                arousal: 0.3 * playfulness,
                energy: -0.015,
                attachment: 0.035 * warmth),
            PetStimulus.RespondToUser => Create(
                PetCommand.Greet,
                "我在，正在听你说",
                policyAllowsSpeech,
                valence: 0.1 * warmth,
                arousal: 0.08,
                energy: 0,
                attachment: 0.025 * warmth),
            _ => throw new ArgumentOutOfRangeException(nameof(stimulus))
        };

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
        double attachment) => new(
        command,
        message,
        shouldSpeak,
        new PetEmotionState(
            Math.Clamp(Emotion.Valence + valence, -1, 1),
            Math.Clamp(Emotion.Arousal + arousal, 0, 1),
            Math.Clamp(Emotion.Energy + energy, 0, 1),
            Math.Clamp(Emotion.Attachment + attachment, 0, 1)));

    private static double Approach(double value, double target, double maximumDelta) =>
        value < target
            ? Math.Min(target, value + maximumDelta)
            : Math.Max(target, value - maximumDelta);
}
