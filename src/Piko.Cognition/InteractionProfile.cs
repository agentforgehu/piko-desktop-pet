namespace Piko.Cognition;

/// <summary>Stable expression preferences, kept separate from short-lived affect state.</summary>
public sealed record InteractionProfile(double Warmth = 0.82, double Playfulness = 0.68, double Restraint = 0.78)
{
    public InteractionProfile Normalize() => this with
    {
        Warmth = Clamp(Warmth),
        Playfulness = Clamp(Playfulness),
        Restraint = Clamp(Restraint)
    };

    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5;
}
