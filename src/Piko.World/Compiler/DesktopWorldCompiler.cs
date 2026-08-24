using Piko.World.Geometry;
using Piko.World.Model;

namespace Piko.World.Compiler;

public sealed class DesktopWorldCompiler
{
    public DesktopWorld Compile(
        DesktopSnapshot snapshot,
        WorldCompilerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options ??= new WorldCompilerOptions();

        if (snapshot.SchemaVersion != DesktopSnapshot.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Snapshot schema {snapshot.SchemaVersion} is not supported.");
        }

        var surfaces = new List<Surface>();
        var visibleOccluders = snapshot.Windows
            .Where(window =>
                window.IsEligible &&
                window.IsVisible &&
                !window.IsMinimized &&
                !window.IsCloaked)
            .ToArray();

        foreach (var window in snapshot.Windows
                     .Where(window => window.IsEligible && !window.Bounds.IsEmpty)
                     .OrderBy(window => window.ZOrder))
        {
            var candidate = new PixelInterval(window.Bounds.Left, window.Bounds.Right);
            var cutters = visibleOccluders
                .Where(occluder =>
                    occluder.ZOrder < window.ZOrder &&
                    occluder.Bounds.CoversY(window.Bounds.Top))
                .Select(occluder => new PixelInterval(
                    occluder.Bounds.Left,
                    occluder.Bounds.Right));

            var segments = IntervalMath.Subtract(candidate, cutters)
                .Where(segment => segment.Length >= options.MinimumSurfaceWidth)
                .OrderBy(segment => segment.Start)
                .ToArray();

            for (var index = 0; index < segments.Length; index++)
            {
                surfaces.Add(new Surface(
                    $"{window.Id}:top:{index}",
                    SurfaceKind.WindowTop,
                    segments[index],
                    window.Bounds.Top,
                    window.MonitorId,
                    window.Id,
                    window.ZOrder));
            }
        }

        if (options.IncludeMonitorFloors)
        {
            foreach (var monitor in snapshot.Monitors)
            {
                if (monitor.WorkArea.Width < options.MinimumSurfaceWidth)
                {
                    continue;
                }

                surfaces.Add(new Surface(
                    $"monitor:{monitor.Id}:floor",
                    SurfaceKind.MonitorFloor,
                    new PixelInterval(monitor.WorkArea.Left, monitor.WorkArea.Right),
                    monitor.WorkArea.Bottom,
                    monitor.Id,
                    null,
                    int.MaxValue));
            }
        }

        return new DesktopWorld(snapshot, surfaces);
    }
}
