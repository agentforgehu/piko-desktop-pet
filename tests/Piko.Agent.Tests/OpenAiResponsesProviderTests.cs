using System.Net;
using System.Text;
using System.Text.Json;
using Piko.Agent.Models;

namespace Piko.Agent.Tests;

public sealed class OpenAiResponsesProviderTests
{
    [Fact]
    public async Task MissingKeyFailsClosedWithoutSendingNetworkRequest()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not send"));
        var provider = new OpenAiResponsesProvider(
            new HttpClient(handler),
            new StaticKeySource(null),
            Options());

        var result = await provider.CompleteAsync(Request(), CancellationToken.None);

        Assert.False(result.Available);
        Assert.Equal("api_key_unavailable", result.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SendsNonStoredBoundedStructuredResponsesRequest()
    {
        const string plan = "{\"message\":\"I can inspect the status.\",\"toolCalls\":[]}";
        var responseJson = JsonSerializer.Serialize(new
        {
            output = new[]
            {
                new
                {
                    content = new[] { new { type = "output_text", text = plan } }
                }
            }
        });
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        var provider = new OpenAiResponsesProvider(
            new HttpClient(handler),
            new StaticKeySource("secret-test-key"),
            Options());

        var result = await provider.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(result.Available);
        Assert.Equal(plan, result.Text);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("secret-test-key", handler.AuthorizationParameter);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal(800, body.RootElement.GetProperty("max_output_tokens").GetInt32());
        var format = body.RootElement.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
    }

    [Fact]
    public void RemotePlainHttpEndpointIsRejected()
    {
        var options = Options() with { Endpoint = new Uri("http://example.com/v1/") };

        Assert.Throws<ArgumentException>(() => new OpenAiResponsesProvider(
            new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            new StaticKeySource("key"),
            options));
    }

    private static OpenAiResponsesOptions Options() => new()
    {
        Endpoint = new Uri("https://api.openai.com/v1/"),
        Model = "test-model",
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static AiModelRequest Request() => new("system", "safe context", "help");

    private sealed class StaticKeySource(string? key) : IAiApiKeySource
    {
        public ValueTask<string?> GetApiKeyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(key);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? Body { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return response(request);
        }
    }
}
