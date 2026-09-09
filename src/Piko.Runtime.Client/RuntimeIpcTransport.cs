using System.Text;

namespace Piko.Runtime.Ipc;

public static class RuntimeIpcTransport
{
    public const int MaximumRequestCharacters = 65_536;
    // A 32,000-character tool result can expand sixfold when JSON-escaped.
    public const int MaximumResponseCharacters = 262_144;
    public static readonly TimeSpan PlanTimeout = TimeSpan.FromSeconds(100);
    public static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(35);

    public static async Task<string?> ReadLineAsync(
        StreamReader reader, int maximumCharacters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCharacters, 1);
        var line = new StringBuilder();
        var character = new char[1];
        // StreamReader buffers the underlying pipe; bound the frame before allocation grows.
        while (await reader.ReadAsync(character.AsMemory(), cancellationToken).ConfigureAwait(false) != 0)
        {
            if (character[0] == '\n')
            {
                if (line.Length > 0 && line[^1] == '\r') line.Length--;
                return line.ToString();
            }
            if (line.Length >= maximumCharacters)
                throw new InvalidDataException("runtime_frame_too_large");
            line.Append(character[0]);
        }
        if (line.Length != 0) throw new InvalidDataException("runtime_frame_incomplete");
        return null;
    }
}
