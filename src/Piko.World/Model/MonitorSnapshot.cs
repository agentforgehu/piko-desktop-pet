using Piko.World.Geometry;

namespace Piko.World.Model;

public sealed record MonitorSnapshot(
    string Id,
    PixelRect Bounds,
    PixelRect WorkArea,
    double DpiX,
    double DpiY,
    bool IsPrimary);
