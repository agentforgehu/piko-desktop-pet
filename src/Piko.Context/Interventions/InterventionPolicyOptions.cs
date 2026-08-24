namespace Piko.Context.Interventions;

public sealed record InterventionPolicyOptions
{
    public int ProactiveSpeechLimitPerHour { get; init; } = 2;
    public int BuildFailuresBeforeOffer { get; init; } = 3;
    public TimeSpan SameActionCooldown { get; init; } = TimeSpan.FromMinutes(20);
}
