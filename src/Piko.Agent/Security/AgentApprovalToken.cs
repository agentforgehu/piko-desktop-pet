namespace Piko.Agent.Security;

public sealed record AgentApprovalToken(
    Guid TokenId,
    Guid InvocationId,
    string ToolName,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);
