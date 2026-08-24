using System.Text;
using System.Text.Json;
using Piko.Context.Events;

namespace Piko.Agent.Tools.Implementations;

public sealed class WorkspaceFileReadTool : IAgentTool
{
    private const int DefaultMaximumCharacters = 32_768;
    private const int AbsoluteMaximumCharacters = 65_536;

    public AgentToolDescriptor Descriptor { get; } = new(
        "workspace.file.read",
        "Read one explicitly named UTF-8 text file inside the approved workspace root.",
        AgentToolRisk.ReadOnly,
        ContextCapability.AgentRead,
        InputJsonSchema: """
        {
          "type":"object",
          "additionalProperties":false,
          "properties":{
            "relativePath":{"type":"string"},
            "maximumCharacters":{"type":"string"}
          },
          "required":["relativePath"]
        }
        """);

    public async ValueTask<AgentToolResult> ExecuteAsync(
        AgentToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (!invocation.Arguments.TryGetValue("relativePath", out var relativePath) ||
            string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return new AgentToolResult(false, "A relative file path is required", string.Empty);
        }

        var maximumCharacters = DefaultMaximumCharacters;
        if (invocation.Arguments.TryGetValue("maximumCharacters", out var maximumText) &&
            (!int.TryParse(maximumText, out maximumCharacters) ||
             maximumCharacters is < 1 or > AbsoluteMaximumCharacters))
        {
            return new AgentToolResult(false, "Invalid maximum character limit", string.Empty);
        }

        var root = Path.GetFullPath(invocation.WorkingDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsInside(root, candidate) || !File.Exists(candidate) ||
            !AllReparseTargetsStayInside(root, candidate))
        {
            return new AgentToolResult(false, "File is outside the approved workspace or unavailable", string.Empty);
        }

        var builder = new StringBuilder(Math.Min(maximumCharacters, 8192));
        var buffer = new char[4096];
        await using var stream = new FileStream(
            candidate,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        var truncated = false;
        while (builder.Length < maximumCharacters)
        {
            var remaining = Math.Min(buffer.Length, maximumCharacters - builder.Length);
            var read = await reader.ReadAsync(buffer.AsMemory(0, remaining), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.AsSpan(0, read).Contains('\0'))
            {
                return new AgentToolResult(false, "Binary files are not supported", string.Empty);
            }

            builder.Append(buffer, 0, read);
        }

        if (!reader.EndOfStream)
        {
            truncated = true;
        }

        return new AgentToolResult(
            true,
            truncated ? "Text file read with limit" : "Text file read",
            JsonSerializer.Serialize(new { text = builder.ToString() }),
            truncated);
    }

    private static bool AllReparseTargetsStayInside(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        var current = root;
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (!info.Exists || !info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            var target = info.ResolveLinkTarget(true);
            if (target is null || !IsInside(root, target.FullName))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsInside(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
