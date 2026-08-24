using System.Text.Json;
using Piko.Agent.Execution;

namespace Piko.Runtime;

public sealed class JsonLineAgentAuditSink : IAgentAuditSink, IDisposable
{
    private const long MaximumBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public JsonLineAgentAuditSink(string path)
    {
        _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public async ValueTask WriteAsync(AgentAuditRecord record, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path) && new FileInfo(_path).Length >= MaximumBytes)
            {
                File.Move(_path, _path + ".previous", true);
            }

            await File.AppendAllTextAsync(
                _path,
                JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Audit I/O must not crash Runtime or leak tool arguments through fallback logs.
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _gate.Dispose();
    }
}
