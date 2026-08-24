namespace Piko.Agent.Models;

public sealed record AiModelRequest(
    string SystemInstruction,
    string SanitizedContext,
    string UserRequest,
    int MaximumOutputTokens = 800);

public sealed record AiModelResponse(
    bool Available,
    string Text,
    string Provider,
    string Model,
    string? Error = null);

public interface IAiProvider
{
    ValueTask<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken);
}

public sealed class DisabledAiProvider : IAiProvider
{
    public ValueTask<AiModelResponse> CompleteAsync(
        AiModelRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new AiModelResponse(false, string.Empty, "disabled", "none"));
}
