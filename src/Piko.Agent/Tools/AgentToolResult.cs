namespace Piko.Agent.Tools;

public sealed record AgentToolResult(
    bool Success,
    string Summary,
    string Output,
    bool WasTruncated = false);
