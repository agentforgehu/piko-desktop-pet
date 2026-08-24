namespace Piko.Agent.Execution;

public sealed record AgentExecutionScope
{
    public required IReadOnlyList<string> AllowedRoots { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaximumOutputCharacters { get; init; } = 32_000;

    public bool Contains(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return AllowedRoots.Any(root =>
        {
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        });
    }

    public AgentExecutionScope Validate()
    {
        if (AllowedRoots.Count == 0 || Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(10) ||
            MaximumOutputCharacters is < 256 or > 1_000_000)
        {
            throw new InvalidOperationException("Invalid agent execution scope.");
        }
        return this;
    }
}
