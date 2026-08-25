using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;
using Piko.Runtime.Ipc;
using Piko.Runtime.Security;

namespace Piko.Setup;

internal static class InstallerOperations
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PikoDesktopPet";
    private const string AppPathsKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\Piko.exe";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "PikoDesktopPet";

    internal static string ProductVersion
    {
        get
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return string.IsNullOrWhiteSpace(informational)
                ? "0.2.2"
                : informational.Split('+', 2)[0];
        }
    }

    internal static void Install()
    {
        Directory.CreateDirectory(InstallerLayout.InstallRoot);
        EnsureInstalledProcessesStopped();

        var nonce = Guid.NewGuid().ToString("N");
        var stageRoot = Path.Combine(InstallerLayout.InstallRoot, $".stage-{nonce}");
        var candidate = Path.Combine(InstallerLayout.InstallRoot, $".app-{nonce}");
        var backup = Path.Combine(InstallerLayout.InstallRoot, ".app-previous");

        try
        {
            using var payload = InstallerPayload.OpenEmbedded();
            var extractedApplication = InstallerPayload.Extract(payload, stageRoot);
            Directory.Move(extractedApplication, candidate);
            if (Directory.Exists(stageRoot))
            {
                InstallerLayout.DeleteManagedDirectory(stageRoot);
            }

            if (Directory.Exists(backup))
            {
                InstallerLayout.DeleteManagedDirectory(backup);
            }

            if (Directory.Exists(InstallerLayout.ApplicationDirectory))
            {
                Directory.Move(InstallerLayout.ApplicationDirectory, backup);
            }

            try
            {
                Directory.Move(candidate, InstallerLayout.ApplicationDirectory);
            }
            catch
            {
                if (!Directory.Exists(InstallerLayout.ApplicationDirectory) && Directory.Exists(backup))
                {
                    Directory.Move(backup, InstallerLayout.ApplicationDirectory);
                }

                throw;
            }

            CopyInstallerForUninstall();
            RegisterInstallation();
            if (Directory.Exists(backup))
            {
                InstallerLayout.DeleteManagedDirectory(backup);
            }
        }
        finally
        {
            if (Directory.Exists(stageRoot))
            {
                InstallerLayout.DeleteManagedDirectory(stageRoot);
            }

            if (Directory.Exists(candidate))
            {
                InstallerLayout.DeleteManagedDirectory(candidate);
            }
        }
    }

    internal static void Uninstall(bool purgeData)
    {
        EnsureInstalledProcessesStopped();
        RemoveRegistration();
        if (Directory.Exists(InstallerLayout.ApplicationDirectory))
        {
            InstallerLayout.DeleteManagedDirectory(InstallerLayout.ApplicationDirectory);
        }

        if (purgeData && Directory.Exists(InstallerLayout.UserDataDirectory))
        {
            var expected = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PikoDesktopPet"));
            if (!string.Equals(
                    Path.GetFullPath(InstallerLayout.UserDataDirectory),
                    expected,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to purge an unexpected data directory.");
            }

            Directory.Delete(expected, recursive: true);
            var credentials = new WindowsCredentialStore();
            credentials.Delete(RuntimeSecretNames.OpenAiApiKey);
            credentials.Delete(RuntimeSecretNames.MemoryEncryptionKey);
        }

        if (Directory.Exists(InstallerLayout.InstallRoot))
        {
            InstallerLayout.DeleteManagedDirectory(InstallerLayout.InstallRoot, allowInstallRoot: true);
        }
    }

    internal static void LaunchPiko()
    {
        var executable = Path.Combine(InstallerLayout.ApplicationDirectory, "Piko.exe");
        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = InstallerLayout.ApplicationDirectory,
            UseShellExecute = true
        })?.Dispose();
    }

    private static void EnsureInstalledProcessesStopped()
    {
        var candidates = Process.GetProcessesByName("Piko")
            .Concat(Process.GetProcessesByName("Piko.Runtime"));
        foreach (var process in candidates)
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }

                var processPath = TryGetProcessPath(process);
                if (processPath is null || !InstallerLayout.IsManagedApplicationPath(processPath))
                {
                    continue;
                }

                StopInstalledProcess(process);
            }
        }
    }

    private static void StopInstalledProcess(Process process)
    {
        try
        {
            if (string.Equals(process.ProcessName, "Piko.Runtime", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var client = new RuntimeIpcClient(timeout: TimeSpan.FromSeconds(2));
                    client.StopAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    // A stale or incompatible Runtime may not accept the graceful stop request.
                }

                if (!process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            else
            {
                process.CloseMainWindow();
            }

            if (!process.WaitForExit(5000))
            {
                // A fullscreen-suppressed WPF window has no visible main window to close.
                // The path guard in EnsureInstalledProcessesStopped makes this fallback
                // safe: only Piko binaries from a managed install root reach this point.
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000))
                {
                    throw new InvalidOperationException("请先从托盘退出 Piko，然后重试安装或卸载。");
                }
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // The process exited between discovery and the stop request.
        }
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static void CopyInstallerForUninstall()
    {
        var source = Environment.ProcessPath
                     ?? throw new InvalidOperationException("The setup executable path is unavailable.");
        if (!string.Equals(
                Path.GetFullPath(source),
                Path.GetFullPath(InstallerLayout.InstalledSetupPath),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source, InstallerLayout.InstalledSetupPath, overwrite: true);
        }
    }

    private static void RegisterInstallation()
    {
        var pikoPath = Path.Combine(InstallerLayout.ApplicationDirectory, "Piko.exe");
        ShortcutService.Create(
            InstallerLayout.StartMenuShortcut,
            pikoPath,
            InstallerLayout.ApplicationDirectory);

        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey, writable: true))
        {
            key.SetValue("DisplayName", "Piko Desktop Pet");
            key.SetValue("DisplayVersion", ProductVersion);
            key.SetValue("Publisher", "Piko contributors");
            key.SetValue("InstallLocation", InstallerLayout.InstallRoot);
            key.SetValue("DisplayIcon", $"\"{pikoPath}\",0");
            key.SetValue("UninstallString", $"\"{InstallerLayout.InstalledSetupPath}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{InstallerLayout.InstalledSetupPath}\" --uninstall --silent");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", CalculateInstalledKilobytes(), RegistryValueKind.DWord);
        }

        using var appPath = Registry.CurrentUser.CreateSubKey(AppPathsKey, writable: true);
        appPath.SetValue(string.Empty, pikoPath);
        appPath.SetValue("Path", InstallerLayout.ApplicationDirectory);
    }

    private static int CalculateInstalledKilobytes()
    {
        var bytes = Directory.EnumerateFiles(
                InstallerLayout.InstallRoot,
                "*",
                SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
        return (int)Math.Min(int.MaxValue, Math.Max(1, bytes / 1024));
    }

    private static void RemoveRegistration()
    {
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(AppPathsKey, throwOnMissingSubKey: false);
        using (var runKey = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            runKey?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }

        if (File.Exists(InstallerLayout.StartMenuShortcut))
        {
            File.Delete(InstallerLayout.StartMenuShortcut);
        }
    }
}
