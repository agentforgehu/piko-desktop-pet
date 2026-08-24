using Piko.World.Geometry;

namespace Piko.World.Behavior;

public sealed record PetBodyState(
    PixelPoint Feet,
    PixelPoint Velocity,
    PetMode Mode,
    bool FacingRight,
    string? SupportSurfaceId,
    string? SupportOwnerWindowId,
    double SupportLocalX,
    string? TargetId,
    double? TargetX,
    double ModeElapsedSeconds,
    string Message)
{
    public static PetBodyState Create(PixelPoint feet) =>
        new(
            feet,
            default,
            PetMode.Falling,
            true,
            null,
            null,
            0,
            null,
            null,
            0,
            "正在寻找落脚点");
}
