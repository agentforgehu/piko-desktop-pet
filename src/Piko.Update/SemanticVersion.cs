using System.Text.RegularExpressions;

namespace Piko.Update;

public sealed record SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    IReadOnlyList<string> Prerelease) : IComparable<SemanticVersion>
{
    private static readonly Regex Pattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public bool IsPrerelease => Prerelease.Count > 0;

    public static SemanticVersion Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96)
        {
            throw new FormatException("Semantic version is invalid.");
        }

        var match = Pattern.Match(value);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, out var major) ||
            !int.TryParse(match.Groups[2].Value, out var minor) ||
            !int.TryParse(match.Groups[3].Value, out var patch))
        {
            throw new FormatException("Semantic version is invalid.");
        }

        var prerelease = match.Groups[4].Success
            ? match.Groups[4].Value.Split('.')
            : [];
        if (prerelease.Any(part =>
                part.Length == 0 ||
                (part.All(char.IsDigit) && part.Length > 1 && part[0] == '0')))
        {
            throw new FormatException("Semantic version prerelease identifiers are invalid.");
        }

        return new SemanticVersion(major, minor, patch, prerelease);
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = Major.CompareTo(other.Major);
        if (core == 0) core = Minor.CompareTo(other.Minor);
        if (core == 0) core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;

        if (!IsPrerelease && !other.IsPrerelease) return 0;
        if (!IsPrerelease) return 1;
        if (!other.IsPrerelease) return -1;

        for (var index = 0; index < Math.Max(Prerelease.Count, other.Prerelease.Count); index++)
        {
            if (index >= Prerelease.Count) return -1;
            if (index >= other.Prerelease.Count) return 1;

            var left = Prerelease[index];
            var right = other.Prerelease[index];
            var leftNumeric = int.TryParse(left, out var leftNumber);
            var rightNumeric = int.TryParse(right, out var rightNumber);
            if (leftNumeric && rightNumeric)
            {
                var numeric = leftNumber.CompareTo(rightNumber);
                if (numeric != 0) return numeric;
                continue;
            }

            if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
            var lexical = string.CompareOrdinal(left, right);
            if (lexical != 0) return lexical;
        }

        return 0;
    }
}
