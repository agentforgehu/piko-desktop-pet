namespace Piko.World.Behavior;

/// <summary>Maps bounded homeostatic drives to an autonomous behavior when no user event is active.</summary>
public sealed class PetBehaviorPolicy
{
    private static readonly PetCommand[] ExploratoryCommands =
        [PetCommand.Walk, PetCommand.Climb, PetCommand.Jump, PetCommand.Peek];
    private static readonly PetCommand[] QuietCommands =
        [PetCommand.Walk, PetCommand.Rest, PetCommand.Greet];
    private int _cycle;

    public PetCommand Choose(PetEmotionState state, bool windowExplorationEnabled)
    {
        if (state.Energy < 0.28 || state.StressLoad >= 0.78)
        {
            return PetCommand.Rest;
        }

        if (windowExplorationEnabled && state.CuriosityDrive >= 0.72)
        {
            return ExploratoryCommands[NextIndex(ExploratoryCommands.Length)];
        }

        var commands = windowExplorationEnabled ? ExploratoryCommands : QuietCommands;
        return commands[NextIndex(commands.Length)];
    }

    private int NextIndex(int length) => _cycle++ % length;
}
