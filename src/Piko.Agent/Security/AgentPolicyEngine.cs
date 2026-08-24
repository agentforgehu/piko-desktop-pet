using Piko.Agent.Tools;
using Piko.Context.Privacy;

namespace Piko.Agent.Security;

public sealed class AgentPolicyEngine
{
    private readonly AgentApprovalTokenStore _tokens;

    public AgentPolicyEngine(AgentApprovalTokenStore tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    public AgentExecutionAuthorization Authorize(
        AgentToolDescriptor descriptor,
        AgentToolInvocation invocation,
        PrivacyProfile privacy,
        DateTimeOffset now,
        AgentApprovalToken? approval = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(privacy);

        if (!descriptor.Name.Equals(invocation.ToolName, StringComparison.Ordinal))
        {
            return new AgentExecutionAuthorization(false, "tool_scope_mismatch", false);
        }

        if (privacy.GrantFor(descriptor.RequiredCapability) == PermissionGrant.Denied)
        {
            return new AgentExecutionAuthorization(false, "capability_denied", false);
        }

        if (invocation.DryRun && !descriptor.SupportsDryRun)
        {
            return new AgentExecutionAuthorization(false, "dry_run_not_supported", false);
        }

        if (descriptor.Risk == AgentToolRisk.ReadOnly)
        {
            return new AgentExecutionAuthorization(true, "read_only_allowed", false);
        }

        var approved = _tokens.TryConsume(approval, invocation, now, out var reason);
        return new AgentExecutionAuthorization(approved, reason, approved);
    }
}
