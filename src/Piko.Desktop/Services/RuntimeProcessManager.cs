using System.Diagnostics;
using Piko.Runtime;
using Piko.Runtime.Ipc;

namespace Piko.Desktop.Services;

public sealed class RuntimeProcessManager
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(6);
    private readonly AppLogger _logger;
    private readonly RuntimeIpcClient _client;

    public RuntimeProcessManager(AppLogger logger, RuntimeIpcClient? client = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = client ?? new RuntimeIpcClient(timeout: TimeSpan.FromMilliseconds(700));
    }

    public async Task<RuntimeStatusSnapshot?> EnsureStartedAsync(
        CancellationToken cancellationToken = default)
    {
        var current = await TryGetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (current is not null)
        {
            return current;
        }

        var executable = Path.Combine(AppContext.BaseDirectory, "Piko.Runtime.exe");
        if (!File.Exists(executable))
        {
            _logger.Info("Piko Runtime executable was not found beside Piko.exe");
            return null;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory
            })?.Dispose();
        }
        catch (Exception exception)
        {
            _logger.Error("Could not start Piko Runtime", exception);
            return null;
        }

        var deadline = DateTimeOffset.UtcNow + StartupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(180), cancellationToken).ConfigureAwait(false);
            current = await TryGetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (current is not null)
            {
                _logger.Info($"Piko Runtime connected (version {current.Version})");
                return current;
            }
        }

        _logger.Info("Piko Runtime did not become healthy before the startup deadline");
        return null;
    }

    public async Task<RuntimeStatusSnapshot?> TryGetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _client.GetHealthAsync(cancellationToken).ConfigureAwait(false);
            var heartbeatAge = DateTimeOffset.UtcNow - status.LastHeartbeatAt;
            return status.Health == "healthy" && heartbeatAge < TimeSpan.FromSeconds(5)
                ? status
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public async Task<RuntimeStatusSnapshot?> RestartAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // It is valid to restart when no Runtime is currently available.
        }

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await TryGetStatusAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken).ConfigureAwait(false);
        }

        return await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RuntimeAgentPlanResponse> PlanAgentAsync(
        string userRequest,
        CancellationToken cancellationToken = default)
    {
        var status = await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        if (status is null)
        {
            throw new InvalidOperationException("Piko Runtime is unavailable.");
        }

        return await _client.PlanAgentAsync(userRequest, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RuntimeMemoryListResponse> ListMemoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var status = await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        if (status is null)
        {
            throw new InvalidOperationException("Piko Runtime is unavailable.");
        }

        return await _client.ListMemoriesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RuntimeAgentExecutionResponse> ExecuteReadAgentProposalAsync(
        string proposalId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var status = await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        if (status is null)
        {
            throw new InvalidOperationException("Piko Runtime is unavailable.");
        }

        return await _client.ExecuteReadProposalAsync(
            proposalId,
            workingDirectory,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteAllMemoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var status = await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        if (status is null)
        {
            throw new InvalidOperationException("Piko Runtime is unavailable.");
        }

        return await _client.DeleteAllMemoriesAsync(cancellationToken).ConfigureAwait(false);
    }
}
