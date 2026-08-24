using Piko.World.Compiler;
using Piko.World.Geometry;
using Piko.World.Model;

namespace Piko.World.Tests;

public sealed class DesktopWorldCompilerTests
{
    private readonly DesktopWorldCompiler _compiler = new();

    [Fact]
    public void Compile_EmitsWholeTopWhenUnoccluded()
    {
        var world = _compiler.Compile(Snapshot(
            Window("base", new PixelRect(100, 200, 900, 800), zOrder: 1)));

        var surface = Assert.Single(world.Surfaces, item => item.OwnerWindowId == "base");
        Assert.Equal(new PixelInterval(100, 900), surface.Horizontal);
        Assert.Equal(200, surface.Y);
    }

    [Fact]
    public void Compile_SplitsCoveredTopUsingZOrder()
    {
        var world = _compiler.Compile(Snapshot(
            Window("front", new PixelRect(400, 100, 700, 500), zOrder: 0),
            Window("base", new PixelRect(100, 200, 900, 800), zOrder: 1)));

        var surfaces = world.Surfaces
            .Where(item => item.OwnerWindowId == "base")
            .OrderBy(item => item.Horizontal.Start)
            .Select(item => item.Horizontal)
            .ToArray();

        Assert.Equal(
            new[] { new PixelInterval(100, 400), new PixelInterval(700, 900) },
            surfaces);
    }

    [Fact]
    public void Compile_DropsFullyCoveredTop()
    {
        var world = _compiler.Compile(Snapshot(
            Window("front", new PixelRect(0, 0, 1000, 500), zOrder: 0),
            Window("base", new PixelRect(100, 200, 900, 800), zOrder: 1)));

        Assert.DoesNotContain(world.Surfaces, item => item.OwnerWindowId == "base");
    }

    [Fact]
    public void Compile_ExcludedWindowDoesNotCutEligibleSurface()
    {
        var world = _compiler.Compile(Snapshot(
            Window("tool", new PixelRect(400, 100, 700, 500), zOrder: 0, isEligible: false),
            Window("base", new PixelRect(100, 200, 900, 800), zOrder: 1)));

        var surface = Assert.Single(world.Surfaces, item => item.OwnerWindowId == "base");
        Assert.Equal(new PixelInterval(100, 900), surface.Horizontal);
    }

    private static DesktopSnapshot Snapshot(params WindowSnapshot[] windows)
    {
        var monitor = new MonitorSnapshot(
            "primary",
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040),
            96,
            96,
            true);

        return DesktopSnapshot.Create(
            new[] { monitor },
            windows,
            new PixelPoint(0, 0),
            DateTimeOffset.UnixEpoch);
    }

    private static WindowSnapshot Window(
        string id,
        PixelRect bounds,
        int zOrder,
        bool isEligible = true) =>
        new(
            id,
            bounds,
            zOrder,
            true,
            false,
            false,
            false,
            isEligible,
            isEligible ? null : "test-exclusion",
            "primary",
            96,
            96);
}
