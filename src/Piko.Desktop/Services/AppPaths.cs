namespace Piko.Desktop.Services;

public sealed class AppPaths
{
    public AppPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PikoDesktopPet");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string LogFile => Path.Combine(Root, "piko.log");

    public string DeviceStateFile => Path.Combine(Root, "device-state.json");
}
