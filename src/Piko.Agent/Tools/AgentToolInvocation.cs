using System.Collections.ObjectModel;

namespace Piko.Agent.Tools;

public sealed record AgentToolInvocation
{
    private AgentToolInvocation(
        Guid invocationId,
        string toolName,
        IReadOnlyDictionary<string, string> arguments,
        string workingDirectory,
        DateTimeOffset requestedAt,
        bool dryRun)
    {
        InvocationId = invocationId;
        ToolName = toolName;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        RequestedAt = requestedAt;
        DryRun = dryRun;
    }

    public Guid InvocationId { get; }
    public string ToolName { get; }
    public IReadOnlyDictionary<string, string> Arguments { get; }
    public string WorkingDirectory { get; }
    public DateTimeOffset RequestedAt { get; }
    public bool DryRun { get; }

    public static AgentToolInvocation Create(
        string toolName,
        string workingDirectory,
        DateTimeOffset requestedAt,
        IReadOnlyDictionary<string, string>? arguments = null,
        bool dryRun = false,
        Guid? invocationId = null)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("Tool name is required.", nameof(toolName));
        }
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException("Working directory is required.", nameof(workingDirectory));
        }

        var values = arguments is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(arguments, StringComparer.Ordinal);
        if (values.Count > 64 || values.Any(item => item.Key.Length > 128 || item.Value.Length > 8192))
        {
            throw new ArgumentException("Tool arguments exceed production limits.", nameof(arguments));
        }

        return new AgentToolInvocation(
            invocationId ?? Guid.NewGuid(),
            toolName,
            new ReadOnlyDictionary<string, string>(values),
            Path.GetFullPath(workingDirectory),
            requestedAt,
            dryRun);
    }
}
