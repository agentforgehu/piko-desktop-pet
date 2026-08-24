namespace Piko.World.Geometry;

public readonly record struct PixelRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Math.Max(0, Right - Left);

    public double Height => Math.Max(0, Bottom - Top);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool CoversY(double y) => Top <= y && y < Bottom;

    public static PixelRect Union(IEnumerable<PixelRect> rectangles)
    {
        var items = rectangles.Where(rectangle => !rectangle.IsEmpty).ToArray();
        if (items.Length == 0)
        {
            return default;
        }

        return new PixelRect(
            items.Min(rectangle => rectangle.Left),
            items.Min(rectangle => rectangle.Top),
            items.Max(rectangle => rectangle.Right),
            items.Max(rectangle => rectangle.Bottom));
    }
}
