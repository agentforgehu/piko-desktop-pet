using Piko.Agent.Execution;
using Piko.Agent.Security;
using Piko.Agent.Tools;
using Piko.Agent.Tools.Implementations;
using Piko.Context.Events;
using Piko.Context.Privacy;

namespace Piko.Agent.Tests;

public sealed class ProductionToolTests
{
    [Fact]
    public async Task WorkspaceReadReturnsBoundedTextInsideRoot()
    {
        var root = CreateRoot();
        var path = Path.Combine(root, "notes.txt");
        await File.WriteAllTextAsync(path, "hello Piko");
        var tool = new WorkspaceFileReadTool();
        var invocation = AgentToolInvocation.Create(
            tool.Descriptor.Name,
            root,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string> { ["relativePath"] = "notes.txt" });

        try
        {
            var result = await tool.ExecuteAsync(invocation, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("hello Piko", result.Output);
            Assert.DoesNotContain(path, result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WorkspaceReadRejectsTraversalOutsideRoot()
    {
        var root = CreateRoot();
        var tool = new WorkspaceFileReadTool();
        var invocation = AgentToolInvocation.Create(
            tool.Descriptor.Name,
            root,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string> { ["relativePath"] = "..\\outside.txt" });

        try
        {
            var result = await tool.ExecuteAsync(invocation, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Empty(result.Output);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GitStatusParserReturnsCountsWithoutPaths()
    {
        const string status = """
        # branch.oid abc123
        # branch.head feature/private-name
        1 M. N... 100644 100644 100644 abc abc src/secret.cs
        1 .M N... 100644 100644 100644 abc abc docs/private.md
        u UU N... 100644 100644 100644 100644 abc abc abc conflict.txt
        """;

        var summary = GitStatusSummaryParser.Parse(status);

        Assert.Equal("feature/private-name", summary.Branch);
        Assert.Equal(1, summary.StagedFiles);
        Assert.Equal(1, summary.ChangedFiles);
        Assert.Equal(1, summary.ConflictedFiles);
        Assert.DoesNotContain("secret.cs", summary.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutorConvertsUnexpectedToolExceptionToSafeFailure()
    {
        var root = CreateRoot();
        var registry = new AgentToolRegistry();
        registry.Register(new ThrowingTool());
        var executor = new AgentExecutor(
            registry,
            new AgentPolicyEngine(new AgentApprovalTokenStore()));
        var invocation = AgentToolInvocation.Create("test.throw", root, DateTimeOffset.UtcNow);

        try
        {
            var result = await executor.ExecuteAsync(
                invocation,
                PrivacyProfile.LocalFirst().WithGrant(ContextCapability.AgentRead, PermissionGrant.AllowSession),
                new AgentExecutionScope { AllowedRoots = new[] { root } },
                DateTimeOffset.UtcNow);

            Assert.False(result.Success);
            Assert.Equal("Agent tool failed safely", result.Summary);
            Assert.Empty(result.Output);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PikoAgentToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class ThrowingTool : IAgentTool
    {
        public AgentToolDescriptor Descriptor { get; } = new(
            "test.throw",
            "Throws for exception isolation testing.",
            AgentToolRisk.ReadOnly,
            ContextCapability.AgentRead);

        public ValueTask<AgentToolResult> ExecuteAsync(
            AgentToolInvocation invocation,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("secret internal failure");
    }
}
