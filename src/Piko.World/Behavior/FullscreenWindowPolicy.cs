using Piko.World.Geometry;

namespace Piko.World.Behavior;

public static class FullscreenWindowPolicy
{
    public static bool CoversMonitor(
        PixelRect windowBounds,
        PixelRect monitorBounds,
        double tolerance = 2)
    {
        if (tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }

        if (windowBounds.IsEmpty || monitorBounds.IsEmpty)
        {
            return false;
        }

        return windowBounds.Left <= monitorBounds.Left + tolerance &&
               windowBounds.Top <= monitorBounds.Top + tolerance &&
               windowBounds.Right >= monitorBounds.Right - tolerance &&
               windowBounds.Bottom >= monitorBounds.Bottom - tolerance;
    }
}
