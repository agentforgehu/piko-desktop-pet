using Piko.World.Geometry;

namespace Piko.World.Model;

public sealed record WindowSnapshot(
    string Id,
    PixelRect Bounds,
    int ZOrder,
    bool IsVisible,
    bool IsMinimized,
    bool IsMaximized,
    bool IsCloaked,
    bool IsEligible,
    string? ExclusionReason,
    string MonitorId,
    double DpiX,
    double DpiY,
    string? ClassName = null);
