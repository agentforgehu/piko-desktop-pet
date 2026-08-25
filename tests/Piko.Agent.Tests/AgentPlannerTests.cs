using Piko.Agent.Models;
using Piko.Agent.Planning;
using Piko.Agent.Tools;
using Piko.Context.Events;

namespace Piko.Agent.Tests;

public sealed class AgentPlannerTests
{
    [Fact]
    public async Task ValidPlanIsReturnedButToolIsNotExecuted()
    {
        var tool = new CountingTool();
        var registry = new AgentToolRegistry();
        registry.Register(tool);
        var provider = new StaticProvider(
            "{\"message\":\"I can check that.\",\"emotion\":\"concerned\",\"action\":\"concern\",\"toolCalls\":[{\"toolName\":\"git.status\",\"rationale\":\"Inspect summary\",\"arguments\":[]}]}" );
        var planner = new AgentPlanner(provider, registry);

        var result = await planner.PlanAsync("situation:CodingBlocked", "What failed?");

        Assert.True(result.Available);
        Assert.Equal("concerned", result.Emotion);
        Assert.Equal("concern", result.Action);
        Assert.Single(result.ToolCalls);
        Assert.Equal("git.status", result.ToolCalls[0].ToolName);
        Assert.Equal(0, tool.ExecutionCount);
    }

    [Fact]
    public async Task UnknownToolProposalFailsClosed()
    {
        var registry = new AgentToolRegistry();
        registry.Register(new CountingTool());
        var provider = new StaticProvider(
            "{\"message\":\"Trying.\",\"emotion\":\"neutral\",\"action\":\"listen\",\"toolCalls\":[{\"toolName\":\"shell.unrestricted\",\"rationale\":\"No\",\"arguments\":[]}]}" );
        var planner = new AgentPlanner(provider, registry);

        var result = await planner.PlanAsync("safe", "Do it");

        Assert.False(result.Available);
        Assert.Equal("invalid_tool_proposal", result.Reason);
    }

    private sealed class StaticProvider(string response) : IAiProvider
    {
        public ValueTask<AiModelResponse> CompleteAsync(
            AiModelRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AiModelResponse(true, response, "fake", "fake"));
    }

    private sealed class CountingTool : IAgentTool
    {
        public int ExecutionCount { get; private set; }

        public AgentToolDescriptor Descriptor { get; } = new(
            "git.status",
            "Read a privacy-safe Git status summary.",
            AgentToolRisk.ReadOnly,
            ContextCapability.AgentRead);

        public ValueTask<AgentToolResult> ExecuteAsync(
            AgentToolInvocation invocation,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(new AgentToolResult(true, "ok", "clean"));
        }
    }
}

