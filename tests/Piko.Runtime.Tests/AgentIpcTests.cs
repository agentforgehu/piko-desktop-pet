using Piko.Agent.Models;
using Piko.Runtime.Ipc;

namespace Piko.Runtime.Tests;

public sealed class AgentIpcTests
{
    [Fact]
    public async Task AgentPlanFailsClosedWhenCloudAiIsDisabled()
    {
        var result = await RunPlanAsync(
            new RuntimeUserSettings(),
            new ThrowingProvider());

        Assert.False(result.Available);
        Assert.Equal("model_disabled", result.Reason);
        Assert.Equal("disabled", result.Provider);
    }

    [Fact]
    public async Task EnabledAgentReturnsValidatedProposalWithoutExecutingIt()
    {
        var provider = new StaticProvider(
            "{\"message\":\"I can inspect Git.\",\"emotion\":\"neutral\",\"action\":\"listen\",\"toolCalls\":[{\"toolName\":\"git.status\",\"rationale\":\"Read counts only\",\"arguments\":[]}]}");
        var result = await RunPlanAsync(
            new RuntimeUserSettings
            {
                CloudAiEnabled = true,
                AgentReadEnabled = true
            },
            provider);

        Assert.True(result.Available);
        var proposal = Assert.Single(result.ToolProposals);
        Assert.Equal("git.status", proposal.ToolName);
        Assert.Equal("ReadOnly", proposal.Risk);
        Assert.True(proposal.PermissionEnabled);
        Assert.False(proposal.RequiresApproval);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task ReadProposalIsOneTimeScopedAndAuditedWithoutArguments()
    {
        var pipeName = $"PikoDesktopPet.Runtime.Test.{Guid.NewGuid():N}";
        var root = Path.Combine(Path.GetTempPath(), "PikoAgentExecutionIpcTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        RuntimeUserSettingsFile.Save(
            Path.Combine(root, "runtime-settings.json"),
            new RuntimeUserSettings
            {
                CloudAiEnabled = true,
                AgentReadEnabled = true
            });
        var provider = new StaticProvider(
            "{\"message\":\"I can inspect Git.\",\"emotion\":\"neutral\",\"action\":\"listen\",\"toolCalls\":[{\"toolName\":\"git.status\",\"rationale\":\"Read counts only\",\"arguments\":[]}]}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var host = new PikoRuntimeHost(
            new RuntimePaths(root),
            pipeName: pipeName,
            aiProvider: provider);
        var hostTask = host.RunAsync(timeout.Token);
        var client = new RuntimeIpcClient(pipeName, TimeSpan.FromSeconds(5));

        try
        {
            var plan = await client.PlanAgentAsync("Check Git", timeout.Token);
            var proposal = Assert.Single(plan.ToolProposals);

            await client.ExecuteReadProposalAsync(proposal.ProposalId, root, timeout.Token);
            var reuse = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.ExecuteReadProposalAsync(proposal.ProposalId, root, timeout.Token));
            Assert.Equal("agent_proposal_invalid_or_expired", reuse.Message);

            var audit = await File.ReadAllTextAsync(Path.Combine(root, "agent-audit.jsonl"), timeout.Token);
            Assert.Contains("git.status", audit, StringComparison.Ordinal);
            Assert.DoesNotContain(root, audit, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("workingDirectory", audit, StringComparison.OrdinalIgnoreCase);
            await client.StopAsync(timeout.Token);
            await hostTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            timeout.Cancel();
            await hostTask.WaitAsync(TimeSpan.FromSeconds(5));
            Directory.Delete(root, true);
        }
    }

    private static async Task<RuntimeAgentPlanResponse> RunPlanAsync(
        RuntimeUserSettings settings,
        IAiProvider provider)
    {
        var pipeName = $"PikoDesktopPet.Runtime.Test.{Guid.NewGuid():N}";
        var root = Path.Combine(Path.GetTempPath(), "PikoAgentIpcTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        RuntimeUserSettingsFile.Save(Path.Combine(root, "runtime-settings.json"), settings);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var host = new PikoRuntimeHost(
            new RuntimePaths(root),
            pipeName: pipeName,
            aiProvider: provider);
        var hostTask = host.RunAsync(timeout.Token);
        var client = new RuntimeIpcClient(pipeName, TimeSpan.FromSeconds(5));

        try
        {
            var result = await client.PlanAgentAsync("What is happening?", timeout.Token);
            await client.StopAsync(timeout.Token);
            await hostTask.WaitAsync(TimeSpan.FromSeconds(5));
            return result;
        }
        finally
        {
            timeout.Cancel();
            await hostTask.WaitAsync(TimeSpan.FromSeconds(5));
            Directory.Delete(root, true);
        }
    }

    private sealed class StaticProvider(string response) : IAiProvider
    {
        public int CallCount { get; private set; }

        public ValueTask<AiModelResponse> CompleteAsync(
            AiModelRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(new AiModelResponse(true, response, "fake", "fake"));
        }
    }

    private sealed class ThrowingProvider : IAiProvider
    {
        public ValueTask<AiModelResponse> CompleteAsync(
            AiModelRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provider must not be called while cloud AI is disabled.");
    }
}

