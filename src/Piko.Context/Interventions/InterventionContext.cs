using Piko.Context.Situations;

namespace Piko.Context.Interventions;

public sealed record InterventionContext(
    SituationState Situation,
    SituationKind PreviousSituation,
    DateTimeOffset Now,
    bool UserRequestedInteraction = false,
    bool QuietHours = false);
