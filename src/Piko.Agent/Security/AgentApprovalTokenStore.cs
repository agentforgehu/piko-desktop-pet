using Piko.Agent.Tools;

namespace Piko.Agent.Security;

public sealed class AgentApprovalTokenStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, AgentApprovalToken> _tokens = new();

    public AgentApprovalToken Issue(
        AgentToolInvocation invocation,
        DateTimeOffset now,
        TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var effectiveLifetime = lifetime ?? TimeSpan.FromMinutes(2);
        if (effectiveLifetime <= TimeSpan.Zero || effectiveLifetime > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        var token = new AgentApprovalToken(
            Guid.NewGuid(),
            invocation.InvocationId,
            invocation.ToolName,
            now,
            now + effectiveLifetime);
        lock (_sync)
        {
            _tokens.Add(token.TokenId, token);
        }
        return token;
    }

    public bool TryConsume(
        AgentApprovalToken? token,
        AgentToolInvocation invocation,
        DateTimeOffset now,
        out string reason)
    {
        if (token is null)
        {
            reason = "approval_required";
            return false;
        }

        lock (_sync)
        {
            if (!_tokens.TryGetValue(token.TokenId, out var stored))
            {
                reason = "approval_unknown_or_consumed";
                return false;
            }

            _tokens.Remove(token.TokenId);
            if (stored != token || stored.InvocationId != invocation.InvocationId ||
                !stored.ToolName.Equals(invocation.ToolName, StringComparison.Ordinal))
            {
                reason = "approval_scope_mismatch";
                return false;
            }

            if (now > stored.ExpiresAt)
            {
                reason = "approval_expired";
                return false;
            }

            reason = "approved";
            return true;
        }
    }
}
