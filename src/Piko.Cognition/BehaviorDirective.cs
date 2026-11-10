namespace Piko.Cognition;

public enum BehaviorDirective
{
    Continue,
    Rest,
    Explore
}

/// <summary>Suggests a bounded behavioral mode; the host remains responsible for permissions.</summary>
public sealed class HomeostasisPolicy(double lowEnergyThreshold = 0.28, double highStressThreshold = 0.78, double explorationThreshold = 0.72)
{
    public BehaviorDirective Choose(AffectState state, bool explorationAllowed)
    {
        state = state.Clamp();
        if (state.Energy < Math.Clamp(lowEnergyThreshold, 0, 1) ||
            state.StressLoad >= Math.Clamp(highStressThreshold, 0, 1))
        {
            return BehaviorDirective.Rest;
        }

        return explorationAllowed && state.CuriosityDrive >= Math.Clamp(explorationThreshold, 0, 1)
            ? BehaviorDirective.Explore
            : BehaviorDirective.Continue;
    }
}
