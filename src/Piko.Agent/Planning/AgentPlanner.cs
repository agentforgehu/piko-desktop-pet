using System.Text.Json;
using Piko.Agent.Models;
using Piko.Agent.Tools;

namespace Piko.Agent.Planning;

public sealed record AgentPlannedToolCall(
    string ToolName,
    string Rationale,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record AgentPlanResult(
    bool Available,
    string Reason,
    string Message,
    IReadOnlyList<AgentPlannedToolCall> ToolCalls,
    string Provider,
    string Model);

public sealed class AgentPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IAiProvider _provider;
    private readonly AgentToolRegistry _registry;

    public AgentPlanner(IAiProvider provider, AgentToolRegistry registry)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async ValueTask<AgentPlanResult> PlanAsync(
        string sanitizedContext,
        string userRequest,
        CancellationToken cancellationToken = default)
    {
        if (sanitizedContext.Length > 32_768 ||
            string.IsNullOrWhiteSpace(userRequest) || userRequest.Length > 8192)
        {
            throw new ArgumentException("Agent planning input exceeds production bounds.");
        }

        var tools = _registry.Descriptors.Select(CreatePromptDescriptor).ToArray();
        var instruction = "You are the planning layer for Piko Desktop Pet. " +
            "Return only the required structured plan. You may propose tools, but you never execute them. " +
            "The local policy engine independently checks every proposal and requires approval for writes. " +
            $"Available tools: {JsonSerializer.Serialize(tools, JsonOptions)}";
        var response = await _provider.CompleteAsync(
            new AiModelRequest(instruction, sanitizedContext, userRequest),
            cancellationToken).ConfigureAwait(false);
        if (!response.Available)
        {
            return new AgentPlanResult(
                false,
                response.Error ?? "provider_unavailable",
                string.Empty,
                Array.Empty<AgentPlannedToolCall>(),
                response.Provider,
                response.Model);
        }

        try
        {
            var wire = JsonSerializer.Deserialize<AgentPlanWire>(response.Text, JsonOptions);
            if (wire?.Message is null || wire.Message.Length > 8192 ||
                wire.ToolCalls is null || wire.ToolCalls.Count > 5)
            {
                return Invalid(response, "invalid_plan_shape");
            }

            var calls = new List<AgentPlannedToolCall>();
            foreach (var call in wire.ToolCalls)
            {
                if (string.IsNullOrWhiteSpace(call.ToolName) ||
                    !_registry.TryGet(call.ToolName, out _) ||
                    string.IsNullOrWhiteSpace(call.Rationale) || call.Rationale.Length > 1024 ||
                    call.Arguments is null || call.Arguments.Count > 64 ||
                    call.Arguments.Any(argument =>
                        string.IsNullOrWhiteSpace(argument.Name) || argument.Name.Length > 128 ||
                        argument.Value is null || argument.Value.Length > 8192) ||
                    call.Arguments.Select(argument => argument.Name).Distinct(StringComparer.Ordinal).Count() !=
                    call.Arguments.Count)
                {
                    return Invalid(response, "invalid_tool_proposal");
                }

                var arguments = call.Arguments.ToDictionary(
                    argument => argument.Name!,
                    argument => argument.Value!,
                    StringComparer.Ordinal);
                calls.Add(new AgentPlannedToolCall(call.ToolName, call.Rationale!, arguments));
            }

            return new AgentPlanResult(
                true,
                "planned",
                wire.Message,
                calls,
                response.Provider,
                response.Model);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return Invalid(response, "invalid_plan_json");
        }
    }

    private static AgentPlanResult Invalid(AiModelResponse response, string reason) => new(
        false,
        reason,
        string.Empty,
        Array.Empty<AgentPlannedToolCall>(),
        response.Provider,
        response.Model);

    private static ToolPromptDescriptor CreatePromptDescriptor(AgentToolDescriptor descriptor)
    {
        using var document = JsonDocument.Parse(descriptor.InputJsonSchema);
        return new ToolPromptDescriptor(
            descriptor.Name,
            descriptor.Description,
            descriptor.Risk.ToString(),
            document.RootElement.Clone());
    }

    private sealed record ToolPromptDescriptor(
        string Name,
        string Description,
        string Risk,
        JsonElement InputSchema);

    private sealed record AgentPlanWire(string? Message, IReadOnlyList<AgentToolCallWire>? ToolCalls);
    private sealed record AgentToolCallWire(
        string? ToolName,
        string? Rationale,
        IReadOnlyList<AgentPlannedArgumentWire>? Arguments);
    private sealed record AgentPlannedArgumentWire(string? Name, string? Value);
}
