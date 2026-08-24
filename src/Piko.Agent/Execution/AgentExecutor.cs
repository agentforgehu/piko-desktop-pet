using Piko.Agent.Security;
using Piko.Agent.Tools;
using Piko.Context.Privacy;

namespace Piko.Agent.Execution;

public sealed class AgentExecutor
{
    private readonly AgentToolRegistry _registry;
    private readonly AgentPolicyEngine _policy;
    private readonly IAgentAuditSink _audit;

    public AgentExecutor(
        AgentToolRegistry registry,
        AgentPolicyEngine policy,
        IAgentAuditSink? audit = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _audit = audit ?? new NullAgentAuditSink();
    }

    public async ValueTask<AgentToolResult> ExecuteAsync(
        AgentToolInvocation invocation,
        PrivacyProfile privacy,
        AgentExecutionScope scope,
        DateTimeOffset now,
        AgentApprovalToken? approval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(privacy);
        scope.Validate();

        if (!_registry.TryGet(invocation.ToolName, out var tool))
        {
            await Audit(false, false, false, "unknown_tool").ConfigureAwait(false);
            return new AgentToolResult(false, "Unknown tool", string.Empty);
        }

        if (!scope.Contains(invocation.WorkingDirectory))
        {
            await Audit(false, false, false, "working_directory_denied").ConfigureAwait(false);
            return new AgentToolResult(false, "Working directory is outside the approved scope", string.Empty);
        }

        var authorization = _policy.Authorize(
            tool.Descriptor,
            invocation,
            privacy,
            now,
            approval);
        if (!authorization.Allowed)
        {
            await Audit(false, false, false, authorization.Reason).ConfigureAwait(false);
            return new AgentToolResult(false, "Agent action was denied", string.Empty);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(scope.Timeout);
        try
        {
            var result = await tool.ExecuteAsync(invocation, timeout.Token).ConfigureAwait(false);
            var truncated = result.Output.Length > scope.MaximumOutputCharacters;
            var safeResult = truncated
                ? result with
                {
                    Output = result.Output[..scope.MaximumOutputCharacters],
                    WasTruncated = true
                }
                : result;
            await Audit(true, true, safeResult.Success, safeResult.Success ? "completed" : "tool_failed")
                .ConfigureAwait(false);
            return safeResult;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await Audit(true, true, false, "timeout").ConfigureAwait(false);
            return new AgentToolResult(false, "Agent action timed out", string.Empty);
        }
        catch (Exception)
        {
            await Audit(true, true, false, "tool_error").ConfigureAwait(false);
            return new AgentToolResult(false, "Agent tool failed safely", string.Empty);
        }

        async ValueTask Audit(bool authorized, bool executed, bool success, string reason)
        {
            await _audit.WriteAsync(new AgentAuditRecord(
                invocation.InvocationId,
                invocation.ToolName,
                now,
                authorized,
                executed,
                success,
                reason), cancellationToken).ConfigureAwait(false);
        }
    }
}
