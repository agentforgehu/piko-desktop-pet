using Piko.Cognition;

namespace Piko.World.Behavior;

/// <summary>Maps bounded homeostatic drives to an autonomous behavior when no user event is active.</summary>
public sealed class PetBehaviorPolicy
{
    private static readonly PetCommand[] ExploratoryCommands =
        [PetCommand.Walk, PetCommand.Climb, PetCommand.Jump, PetCommand.Peek];
    private static readonly PetCommand[] QuietCommands =
        [PetCommand.Walk, PetCommand.Rest, PetCommand.Greet];
    private readonly HomeostasisPolicy _homeostasis = new();
    private int _cycle;

    public PetCommand Choose(PetEmotionState state, bool windowExplorationEnabled)
    {
        var directive = _homeostasis.Choose(new AffectState(
            state.Valence,
            state.Arousal,
            state.Energy,
            state.Attachment,
            state.StressLoad,
            state.CuriosityDrive,
            state.RewardExpectation), windowExplorationEnabled);
        if (directive == BehaviorDirective.Rest)
        {
            return PetCommand.Rest;
        }

        if (directive == BehaviorDirective.Explore)
        {
            return ExploratoryCommands[NextIndex(ExploratoryCommands.Length)];
        }

        var commands = windowExplorationEnabled ? ExploratoryCommands : QuietCommands;
        return commands[NextIndex(commands.Length)];
    }

    private int NextIndex(int length) => _cycle++ % length;
}
