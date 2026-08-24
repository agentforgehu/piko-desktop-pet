namespace Piko.Agent.Security;

public sealed record AgentExecutionAuthorization(bool Allowed, string Reason, bool ApprovalConsumed);
