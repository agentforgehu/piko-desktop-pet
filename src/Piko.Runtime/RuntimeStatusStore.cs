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

    public RuntimeStatusSnapshot? Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<RuntimeStatusSnapshot>(File.ReadAllText(_path), JsonOptions)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
