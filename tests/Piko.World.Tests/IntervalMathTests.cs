using Piko.World.Geometry;

namespace Piko.World.Tests;

public sealed class IntervalMathTests
{
    [Fact]
    public void Subtract_SplitsAroundMiddleCutter()
    {
        var result = IntervalMath.Subtract(
            new PixelInterval(0, 100),
            new[] { new PixelInterval(25, 75) });

        Assert.Equal(
            new[] { new PixelInterval(0, 25), new PixelInterval(75, 100) },
            result);
    }

    [Fact]
    public void Subtract_MergesOverlappingCuttersByProgression()
    {
        var result = IntervalMath.Subtract(
            new PixelInterval(-100, 100),
            new[]
            {
                new PixelInterval(-40, 30),
                new PixelInterval(10, 80)
            });

        Assert.Equal(
            new[] { new PixelInterval(-100, -40), new PixelInterval(80, 100) },
            result);
    }
}
