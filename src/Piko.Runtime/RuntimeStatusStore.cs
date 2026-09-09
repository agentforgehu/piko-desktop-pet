using System.Text.Json;

namespace Piko.Runtime;

public sealed class RuntimeStatusStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;

    public RuntimeStatusStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public void Save(RuntimeStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(status, JsonOptions));
        File.Move(temporary, _path, true);
    }

    // The disk snapshot is diagnostic; a reader holding it open must not stop IPC or the host.
    public bool TrySave(RuntimeStatusSnapshot status)
    {
        try { Save(status); return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false; // The next heartbeat retries with the latest in-memory snapshot.
        }
    }

    public RuntimeStatusSnapshot? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<RuntimeStatusSnapshot>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
