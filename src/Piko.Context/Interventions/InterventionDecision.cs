namespace Piko.Context.Interventions;

public sealed record InterventionDecision(
    InterventionKind Kind,
    string SemanticAction,
    bool ShouldSpeak,
    string Reason)
{
    public static InterventionDecision None(string reason) =>
        new(InterventionKind.None, "none", false, reason);
}
