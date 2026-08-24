namespace Piko.Setup;

internal static class InstallerLayout
{
    internal static string ProgramsRoot => Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs"));

    internal static string InstallRoot => Path.Combine(ProgramsRoot, "PikoDesktopPet");

    internal static string ApplicationDirectory => Path.Combine(InstallRoot, "app");

    internal static string InstalledSetupPath => Path.Combine(InstallRoot, "Piko.Setup.exe");

    internal static string UserDataDirectory => Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PikoDesktopPet"));

    internal static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        "Piko Desktop Pet.lnk");

    internal static bool IsInside(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(
            fullDirectory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    internal static void DeleteManagedDirectory(string path, bool allowInstallRoot = false)
    {
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var installRoot = Path.GetFullPath(InstallRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var allowed = IsInside(fullPath, installRoot) ||
                      (allowInstallRoot && string.Equals(
                          fullPath,
                          installRoot,
                          StringComparison.OrdinalIgnoreCase));
        if (!allowed)
        {
            throw new InvalidOperationException("Refusing to delete a path outside the Piko installation root.");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }
}
