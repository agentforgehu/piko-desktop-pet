namespace Piko.Runtime.Ipc;

public sealed record RuntimeAgentPlanRequest(
    int SchemaVersion,
    string UserRequest)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record RuntimeAgentToolProposal(
    string ProposalId,
    string ToolName,
    string Rationale,
    string Risk,
    bool PermissionEnabled,
    bool RequiresApproval,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record RuntimeAgentPlanResponse(
    bool Available,
    string Reason,
    string Message,
    IReadOnlyList<RuntimeAgentToolProposal> ToolProposals,
    string Provider,
    string Model);

public sealed record RuntimeAgentExecuteReadRequest(
    int SchemaVersion,
    string ProposalId,
    string WorkingDirectory)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record RuntimeAgentExecutionResponse(
    bool Success,
    string Summary,
    string Output,
    bool WasTruncated);
