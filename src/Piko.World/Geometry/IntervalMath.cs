namespace Piko.World.Geometry;

public static class IntervalMath
{
    public static IReadOnlyList<PixelInterval> Subtract(
        PixelInterval source,
        IEnumerable<PixelInterval> cutters)
    {
        if (source.IsEmpty)
        {
            return Array.Empty<PixelInterval>();
        }

        var normalizedCutters = cutters
            .Select(cutter => cutter.Intersect(source))
            .Where(cutter => !cutter.IsEmpty)
            .OrderBy(cutter => cutter.Start)
            .ThenBy(cutter => cutter.End)
            .ToArray();

        if (normalizedCutters.Length == 0)
        {
            return new[] { source };
        }

        var result = new List<PixelInterval>();
        var cursor = source.Start;

        foreach (var cutter in normalizedCutters)
        {
            if (cutter.End <= cursor)
            {
                continue;
            }

            if (cutter.Start > cursor)
            {
                result.Add(new PixelInterval(cursor, Math.Min(cutter.Start, source.End)));
            }

            cursor = Math.Max(cursor, cutter.End);
            if (cursor >= source.End)
            {
                break;
            }
        }

        if (cursor < source.End)
        {
            result.Add(new PixelInterval(cursor, source.End));
        }

        return result.Where(interval => !interval.IsEmpty).ToArray();
    }
}
