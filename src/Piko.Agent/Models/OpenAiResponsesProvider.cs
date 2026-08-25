using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Piko.Agent.Models;

public interface IAiApiKeySource
{
    ValueTask<string?> GetApiKeyAsync(CancellationToken cancellationToken);
}

public sealed class EnvironmentAiApiKeySource : IAiApiKeySource
{
    private readonly string _variableName;

    public EnvironmentAiApiKeySource(string variableName = "PIKO_OPENAI_API_KEY")
    {
        _variableName = string.IsNullOrWhiteSpace(variableName)
            ? throw new ArgumentException("Environment variable name is required.", nameof(variableName))
            : variableName;
    }

    public ValueTask<string?> GetApiKeyAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Environment.GetEnvironmentVariable(_variableName));
}

public sealed record OpenAiResponsesOptions
{
    public Uri Endpoint { get; init; } = new("https://api.openai.com/v1/");
    public string Model { get; init; } = "gpt-5.4-mini";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);
    public int MaximumResponseBytes { get; init; } = 1_048_576;

    public OpenAiResponsesOptions Validate()
    {
        if (!Endpoint.IsAbsoluteUri ||
            (!Endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !(Endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && Endpoint.IsLoopback)) ||
            !string.IsNullOrEmpty(Endpoint.UserInfo) ||
            !string.IsNullOrEmpty(Endpoint.Query) ||
            !string.IsNullOrEmpty(Endpoint.Fragment))
        {
            throw new ArgumentException("AI endpoint must use HTTPS, except for an explicit loopback provider.", nameof(Endpoint));
        }

        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 128 || Model.Any(char.IsControl))
        {
            throw new ArgumentException("AI model identifier is invalid.", nameof(Model));
        }

        if (Timeout < TimeSpan.FromSeconds(1) || Timeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout));
        }

        if (MaximumResponseBytes is < 4096 or > 4_194_304)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumResponseBytes));
        }

        return this;
    }
}

public sealed class OpenAiResponsesProvider : IAiProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object StructuredPlanFormat = new
    {
        type = "json_schema",
        name = "piko_agent_plan",
        strict = true,
        schema = new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                message = new { type = "string" },
                emotion = new { type = "string", @enum = new[] { "neutral", "happy", "concerned", "excited", "calm" } },
                action = new { type = "string", @enum = new[] { "listen", "greet", "concern", "celebrate", "rest" } },
                toolCalls = new
                {
                    type = "array",
                    maxItems = 5,
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            toolName = new { type = "string" },
                            rationale = new { type = "string" },
                            arguments = new
                            {
                                type = "array",
                                maxItems = 64,
                                items = new
                                {
                                    type = "object",
                                    additionalProperties = false,
                                    properties = new
                                    {
                                        name = new { type = "string" },
                                        value = new { type = "string" }
                                    },
                                    required = new[] { "name", "value" }
                                }
                            }
                        },
                        required = new[] { "toolName", "rationale", "arguments" }
                    }
                }
            },
            required = new[] { "message", "emotion", "action", "toolCalls" }
        }
    };

    private readonly HttpClient _httpClient;
    private readonly IAiApiKeySource _apiKeySource;
    private readonly OpenAiResponsesOptions _options;

    public OpenAiResponsesProvider(
        HttpClient httpClient,
        IAiApiKeySource apiKeySource,
        OpenAiResponsesOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKeySource = apiKeySource ?? throw new ArgumentNullException(nameof(apiKeySource));
        _options = (options ?? new OpenAiResponsesOptions()).Validate();
    }

    public async ValueTask<AiModelResponse> CompleteAsync(
        AiModelRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        string? apiKey;
        try
        {
            apiKey = await _apiKeySource.GetApiKeyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new AiModelResponse(false, string.Empty, "openai-responses", _options.Model, "credential_unavailable");
        }
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiModelResponse(false, string.Empty, "openai-responses", _options.Model, "api_key_unavailable");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_options.Endpoint, "responses"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                model = _options.Model,
                instructions = request.SystemInstruction,
                input = $"Sanitized local context:\n{request.SanitizedContext}\n\nUser request:\n{request.UserRequest}",
                max_output_tokens = Math.Clamp(request.MaximumOutputTokens, 64, 4096),
                store = false,
                text = new { format = StructuredPlanFormat }
            }, JsonOptions),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            var json = await ReadLimitedUtf8Async(
                response.Content,
                _options.MaximumResponseBytes,
                timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new AiModelResponse(false, string.Empty, "openai-responses", _options.Model, $"http_{(int)response.StatusCode}");
            }

            using var document = JsonDocument.Parse(json);
            var outputText = ExtractOutputText(document.RootElement);
            return string.IsNullOrWhiteSpace(outputText)
                ? new AiModelResponse(false, string.Empty, "openai-responses", _options.Model, "missing_output_text")
                : new AiModelResponse(true, outputText, "openai-responses", _options.Model);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AiModelResponse(false, string.Empty, "openai-responses", _options.Model, "timeout");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            return new AiModelResponse(false, string.Empty, "openai-responses", _options.Model, "provider_error");
        }
    }

    private static void ValidateRequest(AiModelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SystemInstruction) || request.SystemInstruction.Length > 16_384 ||
            request.SanitizedContext.Length > 32_768 ||
            string.IsNullOrWhiteSpace(request.UserRequest) || request.UserRequest.Length > 8192 ||
            request.MaximumOutputTokens is < 1 or > 4096)
        {
            throw new ArgumentException("AI request exceeds production bounds.", nameof(request));
        }
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var direct) && direct.ValueKind == JsonValueKind.String)
        {
            return direct.GetString();
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text" &&
                    part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }
            }
        }

        return null;
    }

    private static async Task<string> ReadLimitedUtf8Async(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new IOException("AI response exceeded the configured limit.");
            }

            destination.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(destination.ToArray());
    }
}

