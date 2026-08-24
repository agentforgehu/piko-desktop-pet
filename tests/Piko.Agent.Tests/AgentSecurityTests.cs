using Piko.Agent.Execution;
using Piko.Agent.Security;
using Piko.Agent.Tools;
using Piko.Context.Events;
using Piko.Context.Privacy;

namespace Piko.Agent.Tests;

public sealed class AgentSecurityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddHours(1);
    private static readonly string AllowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "piko-agent-tests"));

    [Fact]
    public async Task ReadOnlyTool_IsDeniedByDefaultAndAllowedAfterGrant()
    {
        var (executor, _, tool) = Executor(AgentToolRisk.ReadOnly, ContextCapability.AgentRead);
        var invocation = Invocation(tool.Descriptor.Name);

        var denied = await executor.ExecuteAsync(
            invocation,
            PrivacyProfile.LocalFirst(),
            Scope(),
            Now);
        var allowed = await executor.ExecuteAsync(
            invocation,
            PrivacyProfile.LocalFirst().WithGrant(ContextCapability.AgentRead, PermissionGrant.AllowSession),
            Scope(),
            Now);

        Assert.False(denied.Success);
        Assert.True(allowed.Success);
        Assert.Equal(1, tool.Executions);
    }

    [Fact]
    public async Task WriteTool_RequiresMatchingOneTimeApproval()
    {
        var (executor, tokens, tool) = Executor(AgentToolRisk.ReversibleWrite, ContextCapability.AgentWrite);
        var invocation = Invocation(tool.Descriptor.Name);
        var privacy = PrivacyProfile.LocalFirst()
            .WithGrant(ContextCapability.AgentWrite, PermissionGrant.AllowSession);

        var denied = await executor.ExecuteAsync(invocation, privacy, Scope(), Now);
        var token = tokens.Issue(invocation, Now);
        var allowed = await executor.ExecuteAsync(invocation, privacy, Scope(), Now, token);
        var reused = await executor.ExecuteAsync(invocation, privacy, Scope(), Now, token);

        Assert.False(denied.Success);
        Assert.True(allowed.Success);
        Assert.False(reused.Success);
        Assert.Equal(1, tool.Executions);
    }

    [Fact]
    public async Task WorkingDirectoryOutsideApprovedRoots_IsDeniedBeforeToolRuns()
    {
        var (executor, _, tool) = Executor(AgentToolRisk.ReadOnly, ContextCapability.AgentRead);
        var outside = AgentToolInvocation.Create(
            tool.Descriptor.Name,
            Path.GetPathRoot(AllowedRoot)!,
            Now);

        var result = await executor.ExecuteAsync(
            outside,
            PrivacyProfile.LocalFirst().WithGrant(ContextCapability.AgentRead, PermissionGrant.AllowSession),
            Scope(),
            Now);

        Assert.False(result.Success);
        Assert.Equal(0, tool.Executions);
    }

    [Fact]
    public async Task Output_IsBoundedAndMarkedTruncated()
    {
        var registry = new AgentToolRegistry();
        var tool = new FakeTool(
            new AgentToolDescriptor("test.read", "Test", AgentToolRisk.ReadOnly, ContextCapability.AgentRead),
            new string('x', 2000));
        registry.Register(tool);
        var executor = new AgentExecutor(
            registry,
            new AgentPolicyEngine(new AgentApprovalTokenStore()));

        var result = await executor.ExecuteAsync(
            Invocation(tool.Descriptor.Name),
            PrivacyProfile.LocalFirst().WithGrant(ContextCapability.AgentRead, PermissionGrant.AllowSession),
            Scope(maxOutput: 256),
            Now);

        Assert.True(result.Success);
        Assert.True(result.WasTruncated);
        Assert.Equal(256, result.Output.Length);
    }

    [Fact]
    public void Registry_RejectsDuplicateToolNames()
    {
        var registry = new AgentToolRegistry();
        var descriptor = new AgentToolDescriptor(
            "test.read",
            "Test",
            AgentToolRisk.ReadOnly,
            ContextCapability.AgentRead);
        registry.Register(new FakeTool(descriptor));

        Assert.Throws<InvalidOperationException>(() => registry.Register(new FakeTool(descriptor)));
    }

    private static (AgentExecutor Executor, AgentApprovalTokenStore Tokens, FakeTool Tool) Executor(
        AgentToolRisk risk,
        ContextCapability capability)
    {
        var registry = new AgentToolRegistry();
        var tool = new FakeTool(new AgentToolDescriptor("test.tool", "Test", risk, capability));
        registry.Register(tool);
        var tokens = new AgentApprovalTokenStore();
        return (new AgentExecutor(registry, new AgentPolicyEngine(tokens)), tokens, tool);
    }

    private static AgentToolInvocation Invocation(string name) =>
        AgentToolInvocation.Create(name, AllowedRoot, Now);

    private static AgentExecutionScope Scope(int maxOutput = 1024) => new()
    {
        AllowedRoots = new[] { AllowedRoot },
        Timeout = TimeSpan.FromSeconds(2),
        MaximumOutputCharacters = maxOutput
    };

    private sealed class FakeTool : IAgentTool
    {
        private readonly string _output;

        public FakeTool(AgentToolDescriptor descriptor, string output = "ok")
        {
            Descriptor = descriptor;
            _output = output;
        }

        public AgentToolDescriptor Descriptor { get; }
        public int Executions { get; private set; }

        public ValueTask<AgentToolResult> ExecuteAsync(
            AgentToolInvocation invocation,
            CancellationToken cancellationToken)
        {
            Executions++;
            return ValueTask.FromResult(new AgentToolResult(true, "completed", _output));
        }
    }
}
