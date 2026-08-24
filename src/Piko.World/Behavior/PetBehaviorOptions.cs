namespace Piko.World.Behavior;

public sealed record PetBehaviorOptions
{
    public double Gravity { get; init; } = 1500;
    public double WalkSpeed { get; init; } = 90;
    public double ClimbSpeed { get; init; } = 115;
    public double AutonomousIntervalSeconds { get; init; } = 12;
    public double MinimumSurfaceInset { get; init; } = 18;
    public double PointerApproachRadius { get; init; } = 320;
    public double PointerStopDistance { get; init; } = 72;
}
