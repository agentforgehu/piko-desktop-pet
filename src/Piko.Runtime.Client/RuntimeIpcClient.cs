using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Piko.Runtime.Ipc;

public sealed class RuntimeIpcClient
{
    public const string DefaultPipeName = "PikoDesktopPet.Runtime.v1";
    private const int MaximumResponseCharacters = 65_536;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _pipeName;
    private readonly TimeSpan _timeout;

    public RuntimeIpcClient(string? pipeName = null, TimeSpan? timeout = null)
    {
        _pipeName = ValidatePipeName(pipeName ?? DefaultPipeName);
        _timeout = timeout ?? TimeSpan.FromSeconds(2);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async Task<RuntimeStatusSnapshot> GetHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            RuntimeRequest.Create("health.get"),
            cancellationToken).ConfigureAwait(false);
        if (!response.Success || response.Type != "health" || response.Payload is null)
        {
            throw new InvalidOperationException(response.Error ?? "invalid_health_response");
        }

        var health = response.Payload.Value.Deserialize<RuntimeStatusSnapshot>(JsonOptions);
        if (health is null || health.SchemaVersion != RuntimeStatusSnapshot.CurrentSchemaVersion)
        {
            throw new InvalidDataException("unsupported_health_schema");
        }

        return health;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            RuntimeRequest.Create("runtime.stop"),
            cancellationToken).ConfigureAwait(false);
        if (!response.Success || response.Type != "runtime.stopping")
        {
            throw new InvalidOperationException(response.Error ?? "runtime_stop_failed");
        }
    }

    public async Task<RuntimeContextPublishResult> PublishContextEventAsync(
        RuntimeContextEventEnvelope contextEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contextEvent);
        var request = new RuntimeRequest(
            RuntimeRequest.CurrentSchemaVersion,
            Guid.NewGuid().ToString("N"),
            "context.publish",
            JsonSerializer.SerializeToElement(contextEvent, JsonOptions));
        var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.Success || response.Type != "context.published" || response.Payload is null)
        {
            throw new InvalidOperationException(response.Error ?? "context_publish_failed");
        }

        return response.Payload.Value.Deserialize<RuntimeContextPublishResult>(JsonOptions)
            ?? throw new InvalidDataException("invalid_context_publish_response");
    }

    public async Task<RuntimeAgentPlanResponse> PlanAgentAsync(
        string userRequest,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userRequest) || userRequest.Length > 8192)
        {
            throw new ArgumentException("Agent request must contain 1 to 8192 characters.", nameof(userRequest));
        }

        var payload = new RuntimeAgentPlanRequest(
            RuntimeAgentPlanRequest.CurrentSchemaVersion,
            userRequest);
        var request = new RuntimeRequest(
            RuntimeRequest.CurrentSchemaVersion,
            Guid.NewGuid().ToString("N"),
            "agent.plan",
            JsonSerializer.SerializeToElement(payload, JsonOptions));
        var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.Success || response.Type != "agent.plan" || response.Payload is null)
        {
            throw new InvalidOperationException(response.Error ?? "agent_plan_failed");
        }

        return response.Payload.Value.Deserialize<RuntimeAgentPlanResponse>(JsonOptions)
            ?? throw new InvalidDataException("invalid_agent_plan_response");
    }

    public async Task<RuntimeMemoryListResponse> ListMemoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            RuntimeRequest.Create("memory.list"),
            cancellationToken).ConfigureAwait(false);
        if (!response.Success || response.Type != "memory.list" || response.Payload is null)
        {
            throw new InvalidOperationException(response.Error ?? "memory_list_failed");
        }

        return response.Payload.Value.Deserialize<RuntimeMemoryListResponse>(JsonOptions)
            ?? throw new InvalidDataException("invalid_memory_list_response");
    }

    public async Task<RuntimeAgentExecutionResponse> ExecuteReadProposalAsync(
        string proposalId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(proposalId) ||
            string.IsNullOrWhiteSpace(workingDirectory) || workingDirectory.Length > 1024)
        {
            throw new ArgumentException("Agent proposal and working directory are required.");
        }

        var payload = new RuntimeAgentExecuteReadRequest(
            RuntimeAgentExecuteReadRequest.CurrentSchemaVersion,
            proposalId,
            workingDirectory);
        var request = new RuntimeRequest(
            RuntimeRequest.CurrentSchemaVersion,
            Guid.NewGuid().ToString("N"),
            "agent.execute-read",
            JsonSerializer.SerializeToElement(payload, JsonOptions));
        var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.Success || response.Type != "agent.execution" || response.Payload is null)
        {
            throw new InvalidOperationException(response.Error ?? "agent_execution_failed");
        }

        return response.Payload.Value.Deserialize<RuntimeAgentExecutionResponse>(JsonOptions)
            ?? throw new InvalidDataException("invalid_agent_execution_response");
    }

    public async Task<int> DeleteAllMemoriesAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            RuntimeRequest.Create("memory.delete-all"),
            cancellationToken).ConfigureAwait(false);
        if (!response.Success || response.Type != "memory.deleted" || response.Payload is null ||
            !response.Payload.Value.TryGetProperty("deleted", out var deleted) ||
            !deleted.TryGetInt32(out var count))
        {
            throw new InvalidOperationException(response.Error ?? "memory_delete_failed");
        }

        return count;
    }

    public async Task<RuntimeResponse> SendAsync(
        RuntimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SchemaVersion != RuntimeRequest.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(request.RequestId) ||
            request.RequestId.Length > 128 ||
            string.IsNullOrWhiteSpace(request.Type) ||
            request.Type.Length > 128)
        {
            throw new ArgumentException("Invalid runtime request.", nameof(request));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        await using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);

        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true)
        {
            AutoFlush = true
        };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions))
            .ConfigureAwait(false);
        var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumResponseCharacters)
        {
            throw new InvalidDataException("invalid_runtime_response_size");
        }

        var response = JsonSerializer.Deserialize<RuntimeResponse>(line, JsonOptions)
            ?? throw new InvalidDataException("invalid_runtime_response");
        if (response.SchemaVersion != RuntimeResponse.CurrentSchemaVersion ||
            !string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("runtime_response_mismatch");
        }

        return response;
    }

    private static string ValidatePipeName(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || pipeName.Length > 200 ||
            pipeName.IndexOfAny(['\\', '/']) >= 0)
        {
            throw new ArgumentException("Invalid pipe name.", nameof(pipeName));
        }

        return pipeName;
    }
}
