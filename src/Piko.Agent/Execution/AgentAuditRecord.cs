namespace Piko.Agent.Execution;

public sealed record AgentAuditRecord(
    Guid InvocationId,
    string ToolName,
    DateTimeOffset Timestamp,
    bool Authorized,
    bool Executed,
    bool Success,
    string Reason);

public interface IAgentAuditSink
{
    ValueTask WriteAsync(AgentAuditRecord record, CancellationToken cancellationToken);
}

public sealed class NullAgentAuditSink : IAgentAuditSink
{
    public ValueTask WriteAsync(AgentAuditRecord record, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
