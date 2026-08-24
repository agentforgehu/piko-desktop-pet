namespace Piko.Context.Situations;

public sealed record SituationState(
    SituationKind Kind,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUpdatedAt,
    double Confidence,
    IReadOnlyList<string> Evidence,
    int ConsecutiveBuildFailures,
    bool UserIsActivelyTyping,
    bool IsFullscreen)
{
    public static SituationState Unknown(DateTimeOffset timestamp) => new(
        SituationKind.Unknown,
        timestamp,
        timestamp,
        0,
        Array.Empty<string>(),
        0,
        false,
        false);
}
