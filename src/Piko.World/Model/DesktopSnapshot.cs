using Piko.World.Geometry;

namespace Piko.World.Model;

public sealed record DesktopSnapshot(
    int SchemaVersion,
    DateTimeOffset CapturedAt,
    string CoordinateSpace,
    PixelRect VirtualDesktop,
    IReadOnlyList<MonitorSnapshot> Monitors,
    IReadOnlyList<WindowSnapshot> Windows,
    PixelPoint Cursor)
{
    public const int CurrentSchemaVersion = 1;

    public static DesktopSnapshot Create(
        IEnumerable<MonitorSnapshot> monitors,
        IEnumerable<WindowSnapshot> windows,
        PixelPoint cursor,
        DateTimeOffset? capturedAt = null)
    {
        var monitorList = monitors.ToArray();
        return new DesktopSnapshot(
            CurrentSchemaVersion,
            capturedAt ?? DateTimeOffset.UtcNow,
            "physical_pixel",
            PixelRect.Union(monitorList.Select(monitor => monitor.Bounds)),
            monitorList,
            windows.OrderBy(window => window.ZOrder).ToArray(),
            cursor);
    }
}
