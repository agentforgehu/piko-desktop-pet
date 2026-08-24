using System.Text.Json;
using Piko.World.Behavior;

namespace Piko.Desktop.Services;

public sealed class DeviceStatePublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private long _sequence;
    private PetMode? _lastMode;
    private DateTimeOffset _lastWrite;

    public DeviceStatePublisher(AppPaths paths)
    {
        _paths = paths;
    }

    public void Publish(PetBodyState state)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastMode == state.Mode && now - _lastWrite < TimeSpan.FromSeconds(1))
        {
            return;
        }

        var deviceState = DevicePetState.From(++_sequence, state);
        var temporary = _paths.DeviceStateFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(deviceState, JsonOptions));
        File.Move(temporary, _paths.DeviceStateFile, true);
        _lastMode = state.Mode;
        _lastWrite = now;
    }
}
