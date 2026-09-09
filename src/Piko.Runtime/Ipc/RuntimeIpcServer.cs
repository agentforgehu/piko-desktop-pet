using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Piko.Runtime.Ipc;

public sealed class RuntimeIpcServer
{
    private const int MaximumClients = 4;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly Func<RuntimeRequest, CancellationToken, Task<RuntimeResponse>> _handler;
    private readonly string _pipeName;

    public RuntimeIpcServer(Func<RuntimeRequest, RuntimeResponse> handler, string? pipeName = null)
        : this(Wrap(handler), pipeName) { }

    public RuntimeIpcServer(
        Func<RuntimeRequest, CancellationToken, Task<RuntimeResponse>> handler,
        string? pipeName = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _pipeName = pipeName ?? RuntimeIpcClient.DefaultPipeName;
        if (string.IsNullOrWhiteSpace(_pipeName) || _pipeName.Length > 200 ||
            _pipeName.IndexOfAny(['\\', '/']) >= 0)
            throw new ArgumentException("Invalid pipe name.", nameof(pipeName));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var workers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await Task.WhenAll(Enumerable.Range(0, MaximumClients).Select(async _ =>
        {
            try { await RunListenerAsync(workers.Token).ConfigureAwait(false); }
            catch { workers.Cancel(); throw; }
        })).ConfigureAwait(false);
    }

    private async Task RunListenerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                _pipeName, PipeDirection.InOut, MaximumClients,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await HandleClientAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown, request deadline, or disconnect. Other listeners remain available.
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                // Malformed frames and disconnected clients cannot stop the listener.
            }
        }
    }

    private async Task HandleClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        using var receiveDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        receiveDeadline.CancelAfter(TimeSpan.FromSeconds(5));
        var line = await RuntimeIpcTransport.ReadLineAsync(
            reader, RuntimeIpcTransport.MaximumRequestCharacters, receiveDeadline.Token).ConfigureAwait(false);
        if (line is null) return;

        RuntimeResponse response;
        var requestId = string.Empty;
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestLifetime.CancelAfter(TimeSpan.FromSeconds(105));
        using var stopWatching = new CancellationTokenSource();
        // One request per connection. EOF (or a second frame) cancels in-flight model/tool work.
        var disconnect = WatchDisconnectAsync(reader, requestLifetime, stopWatching.Token);
        try
        {
            var request = JsonSerializer.Deserialize<RuntimeRequest>(line, JsonOptions);
            requestId = request?.RequestId ?? string.Empty;
            response = request is null || string.IsNullOrWhiteSpace(request.RequestId) ||
                request.RequestId.Length > 128 || string.IsNullOrWhiteSpace(request.Type) || request.Type.Length > 128
                ? RuntimeResponse.Fail(string.Empty, "invalid_request")
                : request.SchemaVersion != RuntimeRequest.CurrentSchemaVersion
                    ? RuntimeResponse.Fail(request.RequestId, "unsupported_schema")
                    : await _handler(request, requestLifetime.Token).ConfigureAwait(false);
        }
        catch (JsonException) { response = RuntimeResponse.Fail(requestId, "invalid_json"); }
        catch (OperationCanceledException) when (requestLifetime.IsCancellationRequested) { throw; }
        catch { response = RuntimeResponse.Fail(requestId, "handler_error"); }
        finally
        {
            stopWatching.Cancel();
            await disconnect.ConfigureAwait(false);
        }

        var serialized = JsonSerializer.Serialize(response, JsonOptions);
        if (serialized.Length > RuntimeIpcTransport.MaximumResponseCharacters)
            serialized = JsonSerializer.Serialize(RuntimeResponse.Fail(requestId, "runtime_response_too_large"), JsonOptions);
        // Stop requests receive an acknowledgement even after cancelling the host.
        using var writeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await writer.WriteLineAsync(serialized.AsMemory(), writeDeadline.Token).ConfigureAwait(false);
    }

    private static async Task WatchDisconnectAsync(
        StreamReader reader, CancellationTokenSource requestLifetime, CancellationToken stopWatching)
    {
        try
        {
            await reader.ReadAsync(new char[1].AsMemory(), stopWatching).ConfigureAwait(false);
            requestLifetime.Cancel();
        }
        catch (OperationCanceledException) when (stopWatching.IsCancellationRequested) { }
        catch (IOException) { requestLifetime.Cancel(); }
    }

    private static Func<RuntimeRequest, CancellationToken, Task<RuntimeResponse>> Wrap(
        Func<RuntimeRequest, RuntimeResponse> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return (request, _) => Task.FromResult(handler(request));
    }
}
