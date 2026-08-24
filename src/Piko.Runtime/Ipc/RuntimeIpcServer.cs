using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Piko.Runtime.Ipc;

public sealed class RuntimeIpcServer
{
    private const int MaximumRequestCharacters = 65_536;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Func<RuntimeRequest, CancellationToken, Task<RuntimeResponse>> _handler;
    private readonly string _pipeName;

    public RuntimeIpcServer(
        Func<RuntimeRequest, RuntimeResponse> handler,
        string? pipeName = null)
        : this(Wrap(handler), pipeName)
    {
    }

    public RuntimeIpcServer(
        Func<RuntimeRequest, CancellationToken, Task<RuntimeResponse>> handler,
        string? pipeName = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _pipeName = pipeName ?? RuntimeIpcClient.DefaultPipeName;
        if (string.IsNullOrWhiteSpace(_pipeName) || _pipeName.Length > 200 ||
            _pipeName.IndexOfAny(['\\', '/']) >= 0)
        {
            throw new ArgumentException("Invalid pipe name.", nameof(pipeName));
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                4,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await HandleClientAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // A disconnected or malformed client must not stop the runtime listener.
            }
        }
    }

    private async Task HandleClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true)
        {
            AutoFlush = true
        };
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        RuntimeResponse response;
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumRequestCharacters)
        {
            response = RuntimeResponse.Fail(string.Empty, "invalid_request_size");
        }
        else
        {
            var requestId = string.Empty;
            try
            {
                var request = JsonSerializer.Deserialize<RuntimeRequest>(line, JsonOptions);
                requestId = request?.RequestId ?? string.Empty;
                response = request is null
                    ? RuntimeResponse.Fail(string.Empty, "invalid_request")
                    : request.SchemaVersion != RuntimeResponse.CurrentSchemaVersion
                        ? RuntimeResponse.Fail(request.RequestId, "unsupported_schema")
                        : await _handler(request, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                response = RuntimeResponse.Fail(string.Empty, "invalid_json");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                response = RuntimeResponse.Fail(requestId, "handler_error");
            }
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions))
            .ConfigureAwait(false);
    }

    private static Func<RuntimeRequest, CancellationToken, Task<RuntimeResponse>> Wrap(
        Func<RuntimeRequest, RuntimeResponse> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return (request, _) => Task.FromResult(handler(request));
    }
}
