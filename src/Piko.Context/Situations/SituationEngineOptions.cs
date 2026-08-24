namespace Piko.Context.Situations;

public sealed record SituationEngineOptions
{
    public TimeSpan ReturnedDuration { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan MaximumOutOfOrderAge { get; init; } = TimeSpan.FromSeconds(5);
}
