namespace Piko.World.Behavior;

public sealed record DevicePetState(
    long Sequence,
    DateTimeOffset Timestamp,
    string Mode,
    string EyeShape,
    int LookX,
    int LookY,
    int Brightness,
    string Message)
{
    public static DevicePetState From(long sequence, PetBodyState state) =>
        new(
            sequence,
            DateTimeOffset.UtcNow,
            state.Mode.ToString().ToLowerInvariant(),
            state.Mode switch
            {
                PetMode.Resting => "sleepy",
                PetMode.ObservingTransfer => "focused",
                PetMode.Greeting => "happy",
                PetMode.Falling => "wide",
                _ => "normal"
            },
            state.FacingRight ? 28 : -28,
            state.Mode is PetMode.Climbing ? -18 : 0,
            80,
            state.Message);
}
