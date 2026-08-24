using Piko.World.Geometry;

namespace Piko.World.Model;

public enum SurfaceKind
{
    WindowTop,
    MonitorFloor
}

public sealed record Surface(
    string Id,
    SurfaceKind Kind,
    PixelInterval Horizontal,
    double Y,
    string MonitorId,
    string? OwnerWindowId,
    int OwnerZOrder);
