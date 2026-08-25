using System.Text;
using System.Text.Json;

namespace Piko.Agent.Models;

public sealed record OpenAiCompatibleChatOptions
{
    public Uri Endpoint { get; init; } = new("http://127.0.0.1:11434/v1/");
    public string Model { get; init; } = "local-model";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(90);
    public int MaximumResponseBytes { get; init; } = 1_048_576;

    public OpenAiCompatibleChatOptions Validate()
    {
        if (!Endpoint.IsAbsoluteUri || !Endpoint.IsLoopback ||
            (!Endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !Endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            !string.IsNullOrEmpty(Endpoint.UserInfo) || !string.IsNullOrEmpty(Endpoint.Query) ||
            !string.IsNullOrEmpty(Endpoint.Fragment))
        {
            throw new ArgumentException("Local model endpoint must be an explicit loopback HTTP/HTTPS address.", nameof(Endpoint));
        }

        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 128 || Model.Any(char.IsControl))
        {
            throw new ArgumentException("Local model identifier is invalid.", nameof(Model));
        }

        return this;
    }
}

public sealed class OpenAiCompatibleChatProvider : IAiProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleChatOptions _options;

    public OpenAiCompatibleChatProvider(HttpClient httpClient, OpenAiCompatibleChatOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = (options ?? new OpenAiCompatibleChatOptions()).Validate();
    }

    public async ValueTask<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SystemInstruction) || string.IsNullOrWhiteSpace(request.UserRequest) ||
            request.SystemInstruction.Length > 16_384 || request.SanitizedContext.Length > 32_768 ||
            request.UserRequest.Length > 8192 || request.MaximumOutputTokens is < 1 or > 4096)
        {
            throw new ArgumentException("AI request exceeds production bounds.", nameof(request));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.Endpoint, "chat/completions"));
        message.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = _options.Model,
            messages = new object[]
            {
                new { role = "system", content = request.SystemInstruction },
                new { role = "user", content = $"Sanitized local context:\n{request.SanitizedContext}\n\nUser request:\n{request.UserRequest}" }
            },
            max_tokens = Math.Clamp(request.MaximumOutputTokens, 64, 4096),
            temperature = 0.5,
            response_format = new { type = "json_object" }
        }, JsonOptions), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            var json = await ReadLimitedUtf8Async(response.Content, _options.MaximumResponseBytes, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new AiModelResponse(false, string.Empty, "local-openai-compatible", _options.Model, $"http_{(int)response.StatusCode}");
            }

            using var document = JsonDocument.Parse(json);
            var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return string.IsNullOrWhiteSpace(content)
                ? new AiModelResponse(false, string.Empty, "local-openai-compatible", _options.Model, "missing_output_text")
                : new AiModelResponse(true, content, "local-openai-compatible", _options.Model);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AiModelResponse(false, string.Empty, "local-openai-compatible", _options.Model, "timeout");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return new AiModelResponse(false, string.Empty, "local-openai-compatible", _options.Model, "provider_error");
        }
    }

    private static async Task<string> ReadLimitedUtf8Async(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (destination.Length + read > maximumBytes) throw new IOException("AI response exceeded the configured limit.");
            destination.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(destination.ToArray());
    }
}

