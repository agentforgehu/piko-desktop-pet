namespace Piko.World.Model;

public sealed record DesktopWorld(
    DesktopSnapshot Source,
    IReadOnlyList<Surface> Surfaces);
