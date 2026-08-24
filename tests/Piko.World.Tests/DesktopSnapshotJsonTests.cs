using Piko.World.Geometry;
using Piko.World.Model;
using Piko.World.Serialization;

namespace Piko.World.Tests;

public sealed class DesktopSnapshotJsonTests
{
    [Fact]
    public void RoundTrip_PreservesReplayGeometry()
    {
        var original = DesktopSnapshot.Create(
            new[]
            {
                new MonitorSnapshot(
                    "left",
                    new PixelRect(-1920, 0, 0, 1080),
                    new PixelRect(-1920, 0, 0, 1040),
                    144,
                    144,
                    false)
            },
            new[]
            {
                new WindowSnapshot(
                    "window-1",
                    new PixelRect(-1700, 100, -400, 900),
                    0,
                    true,
                    false,
                    false,
                    false,
                    true,
                    null,
                    "left",
                    144,
                    144)
            },
            new PixelPoint(-900, 500),
            DateTimeOffset.UnixEpoch);

        var replay = DesktopSnapshotJson.Deserialize(DesktopSnapshotJson.Serialize(original));

        Assert.Equal(original.VirtualDesktop, replay.VirtualDesktop);
        Assert.Equal(original.Cursor, replay.Cursor);
        Assert.Equal(original.Monitors.Single(), replay.Monitors.Single());
        Assert.Equal(original.Windows.Single(), replay.Windows.Single());
    }
}
