using Piko.Context.Interventions;
using Piko.Context.Situations;

namespace Piko.Runtime;

public sealed record RuntimeStatusSnapshot(
    int SchemaVersion,
    string Version,
    int ProcessId,
    DateTimeOffset StartedAt,
    DateTimeOffset LastHeartbeatAt,
    string Health,
    SituationKind Situation,
    double SituationConfidence,
    InterventionKind LastIntervention,
    string LastAcceptedEventType,
    string MemoryHealth = "disabled",
    bool CloudAiEnabled = false,
    bool AgentReadEnabled = false,
    long InterventionSequence = 0,
    DateTimeOffset? LastInterventionAt = null,
    string InterventionSemanticAction = "none",
    bool InterventionShouldSpeak = false,
    string InterventionReason = "none")
{
    public const int CurrentSchemaVersion = 2;

    public static RuntimeStatusSnapshot Starting(DateTimeOffset now) => new(
        CurrentSchemaVersion,
        RuntimeProductInfo.Version,
        Environment.ProcessId,
        now,
        now,
        "starting",
        SituationKind.Unknown,
        0,
        InterventionKind.None,
        string.Empty);
}
