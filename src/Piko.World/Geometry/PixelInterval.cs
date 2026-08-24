namespace Piko.World.Geometry;

public readonly record struct PixelInterval(double Start, double End)
{
    public double Length => Math.Max(0, End - Start);

    public bool IsEmpty => Length <= 0;

    public PixelInterval Intersect(PixelInterval other) =>
        new(Math.Max(Start, other.Start), Math.Min(End, other.End));
}
