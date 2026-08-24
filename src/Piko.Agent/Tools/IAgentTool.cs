namespace Piko.Agent.Tools;

public interface IAgentTool
{
    AgentToolDescriptor Descriptor { get; }

    ValueTask<AgentToolResult> ExecuteAsync(
        AgentToolInvocation invocation,
        CancellationToken cancellationToken);
}
