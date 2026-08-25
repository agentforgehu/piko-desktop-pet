using System.Collections.Concurrent;
using System.Text.Json;
using Piko.Agent.Execution;
using Piko.Agent.Models;
using Piko.Agent.Planning;
using Piko.Agent.Security;
using Piko.Agent.Tools;
using Piko.Agent.Tools.Implementations;
using Piko.Context.Events;
using Piko.Context.Interventions;
using Piko.Context.Windows.Observation;
using Piko.Memory;
using Piko.Runtime.Ipc;
using Piko.Runtime.Security;

namespace Piko.Runtime;

public sealed class PikoRuntimeHost
{
    private static readonly JsonSerializerOptions IpcJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly RuntimePaths _paths;
    private readonly IWindowsContextProbe _probe;
    private readonly string? _pipeName;
    private readonly IAiProvider? _aiProviderOverride;
    private readonly SqliteMemoryStore? _memoryStoreOverride;

    private sealed record PendingAgentProposal(
        string ToolName,
        IReadOnlyDictionary<string, string> Arguments,
        DateTimeOffset ExpiresAt);

    public PikoRuntimeHost(
        RuntimePaths paths,
        IWindowsContextProbe? probe = null,
        string? pipeName = null,
        IAiProvider? aiProvider = null,
        SqliteMemoryStore? memoryStore = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _probe = probe ?? new WindowsContextProbe();
        _pipeName = pipeName;
        _aiProviderOverride = aiProvider;
        _memoryStoreOverride = memoryStore;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runtimeToken = shutdown.Token;
        var startedAt = DateTimeOffset.UtcNow;
        var statusStore = new RuntimeStatusStore(_paths.StatusFile);
        var runtimeSettings = RuntimeUserSettingsFile.Load(_paths.SettingsFile);
        using var engine = new ContextRuntimeEngine(runtimeSettings.ToPrivacyProfile());
        SqliteMemoryStore? memoryStore = null;
        var memoryHealth = runtimeSettings.MemoryEnabled ? "starting" : "disabled";
        if (runtimeSettings.MemoryEnabled)
        {
            try
            {
                memoryStore = _memoryStoreOverride ?? new SqliteMemoryStore(
                    _paths.MemoryDatabaseFile,
                    new CredentialMemoryProtectorFactory().Create());
                await memoryStore.PurgeExpiredAsync(DateTimeOffset.UtcNow, runtimeToken).ConfigureAwait(false);
                memoryHealth = "healthy";
            }
            catch
            {
                memoryStore?.Dispose();
                memoryStore = null;
                memoryHealth = "error";
            }
        }
        using var activeMemoryStore = memoryStore;
        var status = RuntimeStatusSnapshot.Starting(startedAt) with
        {
            MemoryHealth = memoryHealth,
            CloudAiEnabled = runtimeSettings.CloudAiEnabled,
            AgentReadEnabled = runtimeSettings.AgentReadEnabled
        };
        statusStore.Save(status);

        using var agentHttpClient = new HttpClient();
        IAiProvider aiProvider = _aiProviderOverride ?? (runtimeSettings.CloudAiEnabled
            ? new OpenAiResponsesProvider(
                agentHttpClient,
                new CredentialAiApiKeySource(),
                new OpenAiResponsesOptions
                {
                    Endpoint = new Uri(runtimeSettings.AiEndpoint),
                    Model = runtimeSettings.AiModel
                })
            : new DisabledAiProvider());
        var agentTools = new AgentToolRegistry();
        agentTools.Register(new GitStatusTool());
        agentTools.Register(new WorkspaceFileReadTool());
        var agentPlanner = new AgentPlanner(aiProvider, agentTools);
        using var agentAudit = new JsonLineAgentAuditSink(_paths.AgentAuditFile);
        var agentExecutor = new AgentExecutor(
            agentTools,
            new AgentPolicyEngine(new AgentApprovalTokenStore()),
            agentAudit);
        var pendingAgentProposals = new ConcurrentDictionary<Guid, PendingAgentProposal>();
        var source = new WindowsContextEventSource(Guid.NewGuid().ToString("N"));
        var ipc = new RuntimeIpcServer(async (request, requestCancellation) =>
        {
            if (request.Type == "health.get")
            {
                return RuntimeResponse.Ok(request.RequestId, "health", status);
            }

            if (request.Type == "runtime.stop")
            {
                var response = RuntimeResponse.Ok(
                    request.RequestId,
                    "runtime.stopping",
                    new { accepted = true });
                shutdown.Cancel();
                return response;
            }

            if (request.Type == "context.publish")
            {
                RuntimeContextEventEnvelope? envelope;
                try
                {
                    envelope = request.Payload?.Deserialize<RuntimeContextEventEnvelope>(IpcJsonOptions);
                }
                catch (JsonException)
                {
                    return RuntimeResponse.Fail(request.RequestId, "invalid_context_payload");
                }

                if (envelope is null)
                {
                    return RuntimeResponse.Fail(request.RequestId, "invalid_context_payload");
                }

                if (!TryAuthorizeExternalEvent(envelope, out var authorizationError))
                {
                    return RuntimeResponse.Fail(request.RequestId, authorizationError!);
                }

                if (!envelope.TryCreateContextEvent(out var contextEvent, out var validationError) ||
                    contextEvent is null)
                {
                    return RuntimeResponse.Fail(request.RequestId, validationError);
                }

                var update = await engine.ProcessAsync(
                    contextEvent,
                    cancellationToken: requestCancellation).ConfigureAwait(false);
                if (update.Accepted)
                {
                    status = ApplyAcceptedUpdate(
                        status,
                        update,
                        contextEvent.Type,
                        DateTimeOffset.UtcNow);
                    if (activeMemoryStore is not null &&
                        TryCreateMemoryDraft(contextEvent, out var draft))
                    {
                        try
                        {
                            await activeMemoryStore.AddAsync(
                                draft,
                                contextEvent.Timestamp,
                                requestCancellation).ConfigureAwait(false);
                        }
                        catch
                        {
                            memoryHealth = "error";
                            status = status with { MemoryHealth = memoryHealth };
                        }
                    }
                }

                return RuntimeResponse.Ok(
                    request.RequestId,
                    "context.published",
                    new RuntimeContextPublishResult(
                        update.Accepted,
                        update.Reason,
                        update.Situation.Kind.ToString(),
                        update.Intervention.Kind.ToString()));
            }

            if (request.Type == "agent.plan")
            {
                RuntimeAgentPlanRequest? agentRequest;
                try
                {
                    agentRequest = request.Payload?.Deserialize<RuntimeAgentPlanRequest>(IpcJsonOptions);
                }
                catch (JsonException)
                {
                    return RuntimeResponse.Fail(request.RequestId, "invalid_agent_payload");
                }

                if (agentRequest is null ||
                    agentRequest.SchemaVersion != RuntimeAgentPlanRequest.CurrentSchemaVersion ||
                    string.IsNullOrWhiteSpace(agentRequest.UserRequest) ||
                    agentRequest.UserRequest.Length > 8192)
                {
                    return RuntimeResponse.Fail(request.RequestId, "invalid_agent_payload");
                }

                if (!runtimeSettings.CloudAiEnabled)
                {
                    return RuntimeResponse.Ok(
                        request.RequestId,
                        "agent.plan",
                        new RuntimeAgentPlanResponse(
                            false,
                            "cloud_ai_disabled",
                            string.Empty,
                            Array.Empty<RuntimeAgentToolProposal>(),
                            "disabled",
                            "none"));
                }

                var currentSituation = engine.CurrentSituation;
                var sanitizedContext = JsonSerializer.Serialize(new
                {
                    situation = currentSituation.Kind.ToString(),
                    confidence = currentSituation.Confidence,
                    evidence = currentSituation.Evidence,
                    consecutiveBuildFailures = currentSituation.ConsecutiveBuildFailures,
                    isActivelyTyping = currentSituation.UserIsActivelyTyping,
                    isFullscreen = currentSituation.IsFullscreen
                });
                var plan = await agentPlanner.PlanAsync(
                    sanitizedContext,
                    agentRequest.UserRequest,
                    requestCancellation).ConfigureAwait(false);
                foreach (var expired in pendingAgentProposals.Where(item => item.Value.ExpiresAt <= DateTimeOffset.UtcNow))
                {
                    pendingAgentProposals.TryRemove(expired.Key, out _);
                }
                var proposals = plan.ToolCalls.Select(call =>
                {
                    agentTools.TryGet(call.ToolName, out var tool);
                    var risk = tool.Descriptor.Risk;
                    var proposalId = Guid.NewGuid();
                    pendingAgentProposals[proposalId] = new PendingAgentProposal(
                        call.ToolName,
                        call.Arguments,
                        DateTimeOffset.UtcNow.AddMinutes(5));
                    return new RuntimeAgentToolProposal(
                        proposalId.ToString("N"),
                        call.ToolName,
                        call.Rationale,
                        risk.ToString(),
                        runtimeSettings.AgentReadEnabled && risk == AgentToolRisk.ReadOnly,
                        risk != AgentToolRisk.ReadOnly,
                        call.Arguments);
                }).ToArray();
                return RuntimeResponse.Ok(
                    request.RequestId,
                    "agent.plan",
                    new RuntimeAgentPlanResponse(
                        plan.Available,
                        plan.Reason,
                        plan.Message,
                        proposals,
                        plan.Provider,
                        plan.Model));
            }

            if (request.Type == "agent.execute-read")
            {
                RuntimeAgentExecuteReadRequest? executionRequest;
                try
                {
                    executionRequest = request.Payload?.Deserialize<RuntimeAgentExecuteReadRequest>(IpcJsonOptions);
                }
                catch (JsonException)
                {
                    return RuntimeResponse.Fail(request.RequestId, "invalid_agent_execution_payload");
                }

                if (executionRequest is null ||
                    executionRequest.SchemaVersion != RuntimeAgentExecuteReadRequest.CurrentSchemaVersion ||
                    !Guid.TryParseExact(executionRequest.ProposalId, "N", out var proposalId) ||
                    string.IsNullOrWhiteSpace(executionRequest.WorkingDirectory) ||
                    executionRequest.WorkingDirectory.Length > 1024)
                {
                    return RuntimeResponse.Fail(request.RequestId, "invalid_agent_execution_payload");
                }

                if (!runtimeSettings.AgentReadEnabled)
                {
                    return RuntimeResponse.Fail(request.RequestId, "agent_read_disabled");
                }

                if (!pendingAgentProposals.TryRemove(proposalId, out var pending) ||
                    pending.ExpiresAt <= DateTimeOffset.UtcNow ||
                    !agentTools.TryGet(pending.ToolName, out var pendingTool) ||
                    pendingTool.Descriptor.Risk != AgentToolRisk.ReadOnly)
                {
                    return RuntimeResponse.Fail(request.RequestId, "agent_proposal_invalid_or_expired");
                }

                string workingDirectory;
                try
                {
                    workingDirectory = Path.GetFullPath(executionRequest.WorkingDirectory);
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
                {
                    return RuntimeResponse.Fail(request.RequestId, "working_directory_invalid");
                }

                if (!Directory.Exists(workingDirectory))
                {
                    return RuntimeResponse.Fail(request.RequestId, "working_directory_unavailable");
                }

                var invocation = AgentToolInvocation.Create(
                    pending.ToolName,
                    workingDirectory,
                    DateTimeOffset.UtcNow,
                    pending.Arguments);
                var result = await agentExecutor.ExecuteAsync(
                    invocation,
                    runtimeSettings.ToPrivacyProfile(),
                    new AgentExecutionScope
                    {
                        AllowedRoots = new[] { workingDirectory },
                        Timeout = TimeSpan.FromSeconds(30),
                        MaximumOutputCharacters = 32_000
                    },
                    DateTimeOffset.UtcNow,
                    cancellationToken: requestCancellation).ConfigureAwait(false);
                return RuntimeResponse.Ok(
                    request.RequestId,
                    "agent.execution",
                    new RuntimeAgentExecutionResponse(
                        result.Success,
                        result.Summary,
                        result.Output,
                        result.WasTruncated));
            }

            if (request.Type == "memory.list")
            {
                if (activeMemoryStore is null)
                {
                    return RuntimeResponse.Ok(
                        request.RequestId,
                        "memory.list",
                        new RuntimeMemoryListResponse(
                            false,
                            runtimeSettings.MemoryEnabled ? "memory_unavailable" : "memory_disabled",
                            Array.Empty<RuntimeMemoryItem>()));
                }

                var memories = await activeMemoryStore.ListAsync(
                    DateTimeOffset.UtcNow,
                    limit: 100,
                    cancellationToken: requestCancellation).ConfigureAwait(false);
                return RuntimeResponse.Ok(
                    request.RequestId,
                    "memory.list",
                    new RuntimeMemoryListResponse(
                        true,
                        "available",
                        memories.Select(memory => new RuntimeMemoryItem(
                            memory.Id.ToString("N"),
                            memory.Kind.ToString(),
                            memory.CreatedAt,
                            memory.ExpiresAt,
                            memory.Sensitivity.ToString(),
                            memory.Summary)).ToArray()));
            }

            if (request.Type == "memory.delete-all")
            {
                int deleted;
                if (activeMemoryStore is null)
                {
                    DeleteDisabledMemoryData(_paths);
                    deleted = 0;
                }
                else
                {
                    deleted = await activeMemoryStore.DeleteAllAsync(requestCancellation).ConfigureAwait(false);
                }
                return RuntimeResponse.Ok(
                    request.RequestId,
                    "memory.deleted",
                    new { deleted });
            }

            return RuntimeResponse.Fail(request.RequestId, "unknown_request_type");
        }, _pipeName);
        var ipcTask = ipc.RunAsync(runtimeToken);

        try
        {
            while (!runtimeToken.IsCancellationRequested)
            {
                var snapshot = _probe.Capture();
                foreach (var contextEvent in source.Diff(snapshot))
                {
                    var update = await engine.ProcessAsync(contextEvent, cancellationToken: runtimeToken)
                        .ConfigureAwait(false);
                    if (update.Accepted)
                    {
                        status = ApplyAcceptedUpdate(
                            status,
                            update,
                            contextEvent.Type,
                            DateTimeOffset.UtcNow);
                    }
                }

                var situation = engine.CurrentSituation;
                status = status with
                {
                    LastHeartbeatAt = DateTimeOffset.UtcNow,
                    Health = "healthy",
                    Situation = situation.Kind,
                    SituationConfidence = situation.Confidence,
                    MemoryHealth = memoryHealth,
                    CloudAiEnabled = runtimeSettings.CloudAiEnabled,
                    AgentReadEnabled = runtimeSettings.AgentReadEnabled
                };
                statusStore.Save(status);
                await Task.Delay(TimeSpan.FromSeconds(1), runtimeToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (runtimeToken.IsCancellationRequested)
        {
            // External cancellation and the authenticated local stop request are normal exits.
        }
        finally
        {
            try
            {
                await ipcTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (runtimeToken.IsCancellationRequested)
            {
                // Normal runtime shutdown.
            }
        }
    }

    private static RuntimeStatusSnapshot ApplyAcceptedUpdate(
        RuntimeStatusSnapshot status,
        ContextRuntimeUpdate update,
        string eventType,
        DateTimeOffset now)
    {
        var next = status with
        {
            LastHeartbeatAt = now,
            Situation = update.Situation.Kind,
            SituationConfidence = update.Situation.Confidence,
            LastAcceptedEventType = eventType
        };
        if (update.Intervention.Kind == InterventionKind.None)
        {
            return next;
        }

        return next with
        {
            LastIntervention = update.Intervention.Kind,
            InterventionSequence = status.InterventionSequence + 1,
            LastInterventionAt = now,
            InterventionSemanticAction = update.Intervention.SemanticAction,
            InterventionShouldSpeak = update.Intervention.ShouldSpeak,
            InterventionReason = update.Intervention.Reason
        };
    }

    private static bool TryAuthorizeExternalEvent(
        RuntimeContextEventEnvelope envelope,
        out string? error)
    {
        error = null;
        if (!string.Equals(envelope.Source, "vscode.extension", StringComparison.Ordinal))
        {
            error = "external_source_denied";
            return false;
        }

        var expectedCapability = envelope.Type switch
        {
            ContextEventTypes.BuildStarted => ContextCapability.DevelopmentActivity,
            ContextEventTypes.BuildCompleted => ContextCapability.DevelopmentActivity,
            ContextEventTypes.TestsCompleted => ContextCapability.DevelopmentActivity,
            ContextEventTypes.DiagnosticsChanged => ContextCapability.DiagnosticsSummary,
            ContextEventTypes.GitActivity => ContextCapability.GitMetadata,
            _ => (ContextCapability?)null
        };
        if (expectedCapability is null ||
            !Enum.TryParse<ContextCapability>(envelope.Capability, true, out var actualCapability) ||
            actualCapability != expectedCapability)
        {
            error = "external_event_capability_denied";
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (envelope.Timestamp < now - TimeSpan.FromMinutes(10) ||
            envelope.Timestamp > now + TimeSpan.FromMinutes(1))
        {
            error = "external_event_time_invalid";
            return false;
        }

        if (envelope.Retention.Equals("persistent", StringComparison.OrdinalIgnoreCase))
        {
            error = "external_event_retention_denied";
            return false;
        }

        return true;
    }

    private static void DeleteDisabledMemoryData(RuntimePaths paths)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.Root));
        foreach (var path in new[]
                 {
                     paths.MemoryDatabaseFile,
                     paths.MemoryDatabaseFile + "-wal",
                     paths.MemoryDatabaseFile + "-shm"
                 })
        {
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Memory data path escaped the Runtime root.");
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        new WindowsCredentialStore().Delete(RuntimeSecretNames.MemoryEncryptionKey);
    }

    private static bool TryCreateMemoryDraft(ContextEvent contextEvent, out MemoryDraft draft)
    {
        if (contextEvent.Type == ContextEventTypes.BuildCompleted &&
            contextEvent.TryGetBoolean("success", out var buildSucceeded))
        {
            draft = new MemoryDraft(
                MemoryKind.Episodic,
                buildSucceeded ? "Build completed successfully" : "Build failed",
                JsonSerializer.Serialize(new { success = buildSucceeded }),
                DataSensitivity.Low,
                contextEvent.Source);
            return true;
        }

        if (contextEvent.Type == ContextEventTypes.TestsCompleted &&
            contextEvent.TryGetInt32("failed", out var failedTests))
        {
            draft = new MemoryDraft(
                MemoryKind.Episodic,
                failedTests == 0
                    ? "Tests completed successfully"
                    : $"Tests completed with {failedTests} failures",
                JsonSerializer.Serialize(new { failed = Math.Max(0, failedTests) }),
                DataSensitivity.Low,
                contextEvent.Source);
            return true;
        }

        draft = null!;
        return false;
    }
}
