using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Piko.Context.Events;

namespace Piko.Agent.Tools.Implementations;

public sealed record GitStatusSummary(
    string Branch,
    int StagedFiles,
    int ChangedFiles,
    int ConflictedFiles);

public static class GitStatusSummaryParser
{
    public static GitStatusSummary Parse(string porcelainV2)
    {
        var branch = "unknown";
        var staged = 0;
        var changed = 0;
        var conflicts = 0;
        foreach (var line in porcelainV2.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                branch = line[14..].Trim();
                continue;
            }

            if (line.StartsWith("u ", StringComparison.Ordinal))
            {
                conflicts++;
                continue;
            }

            if (!(line.StartsWith("1 ", StringComparison.Ordinal) ||
                  line.StartsWith("2 ", StringComparison.Ordinal)))
            {
                continue;
            }

            var statusEnd = line.IndexOf(' ', 2);
            if (statusEnd <= 2)
            {
                continue;
            }

            var status = line.AsSpan(2, statusEnd - 2);
            if (status.Length >= 2)
            {
                staged += status[0] == '.' ? 0 : 1;
                changed += status[1] == '.' ? 0 : 1;
            }
        }

        return new GitStatusSummary(branch, staged, changed, conflicts);
    }
}

public sealed class GitStatusTool : IAgentTool
{
    private const int MaximumProcessOutputCharacters = 2_097_152;

    public AgentToolDescriptor Descriptor { get; } = new(
        "git.status",
        "Read branch and staged, changed, and conflict counts without returning file paths or file content.",
        AgentToolRisk.ReadOnly,
        ContextCapability.AgentRead);

    public async ValueTask<AgentToolResult> ExecuteAsync(
        AgentToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = invocation.WorkingDirectory
            }
        };
        process.StartInfo.ArgumentList.Add("--no-optional-locks");
        process.StartInfo.ArgumentList.Add("status");
        process.StartInfo.ArgumentList.Add("--porcelain=v2");
        process.StartInfo.ArgumentList.Add("--branch");
        process.StartInfo.ArgumentList.Add("--untracked-files=no");
        process.StartInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        try
        {
            if (!process.Start())
            {
                return new AgentToolResult(false, "Git could not be started", string.Empty);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new AgentToolResult(false, "Git is not installed or unavailable", string.Empty);
        }

        var standardOutput = ReadBoundedAsync(
            process.StandardOutput,
            MaximumProcessOutputCharacters,
            cancellationToken);
        var standardError = ReadBoundedAsync(
            process.StandardError,
            16_384,
            cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await standardOutput.ConfigureAwait(false);
            await standardError.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return new AgentToolResult(false, "Working directory is not an accessible Git repository", string.Empty);
            }

            var summary = GitStatusSummaryParser.Parse(output);
            return new AgentToolResult(
                true,
                "Privacy-safe Git status collected",
                JsonSerializer.Serialize(summary));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }

            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(8192, maximumCharacters));
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (builder.Length + read > maximumCharacters)
            {
                throw new IOException("Git output exceeded the privacy-safe processing limit.");
            }

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }
}
