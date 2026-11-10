namespace Piko.Cognition;

/// <summary>
/// Bounded functional interaction state. These dimensions support software policy;
/// they are not claims about subjective experience or human physiology.
/// </summary>
public readonly record struct AffectState(
    double Valence,
    double Arousal,
    double Energy,
    double SocialContinuity,
    double StressLoad,
    double CuriosityDrive,
    double RewardExpectation)
{
    public static AffectState Baseline => new(
        Valence: 0.18,
        Arousal: 0.28,
        Energy: 0.72,
        SocialContinuity: 0.55,
        StressLoad: 0.12,
        CuriosityDrive: 0.45,
        RewardExpectation: 0.5);

    public AffectState Clamp() => new(
        ClampFinite(Valence, -1, 1),
        ClampFinite(Arousal, 0, 1),
        ClampFinite(Energy, 0, 1),
        ClampFinite(SocialContinuity, 0, 1),
        ClampFinite(StressLoad, 0, 1),
        ClampFinite(CuriosityDrive, 0, 1),
        ClampFinite(RewardExpectation, 0, 1));

    private static double ClampFinite(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : 0;
}
