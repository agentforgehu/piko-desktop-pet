using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Piko.Setup;

internal static class Program
{
    private const uint MoveFileDelayUntilReboot = 0x00000004;

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var silent = args.Contains("--silent", StringComparer.OrdinalIgnoreCase);
        var purgeData = args.Contains("--purge-data", StringComparer.OrdinalIgnoreCase);

        try
        {
            if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
            {
                using var payload = InstallerPayload.OpenEmbedded();
                InstallerPayload.Validate(payload);
                return 0;
            }

            if (args.Contains("--uninstall-worker", StringComparer.OrdinalIgnoreCase))
            {
                InstallerOperations.Uninstall(purgeData);
                if (!silent)
                {
                    MessageBox.Show("Piko 已卸载。默认保留了你的本地设置和记忆。", "Piko", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                ScheduleTemporaryUninstallerCleanup();
                return 0;
            }

            if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
            {
                if (!silent)
                {
                    var result = MessageBox.Show(
                        "要卸载 Piko 吗？本地设置和记忆默认保留；若需完全清除，请使用 --purge-data。",
                        "卸载 Piko",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (result != DialogResult.Yes)
                    {
                        return 0;
                    }
                }

                StartUninstallWorker(purgeData, silent);
                return 0;
            }

            if (!silent)
            {
                var result = MessageBox.Show(
                    $"将为当前 Windows 用户安装 Piko Desktop Pet {InstallerOperations.ProductVersion}。继续吗？",
                    "安装 Piko",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (result != DialogResult.Yes)
                {
                    return 0;
                }
            }

            WaitForRequestedProcess(args);
            InstallerOperations.Install();
            if (!silent)
            {
                MessageBox.Show("Piko 已安装完成。", "Piko", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            if (!args.Contains("--no-launch", StringComparer.OrdinalIgnoreCase))
            {
                InstallerOperations.LaunchPiko();
            }

            return 0;
        }
        catch (Exception exception)
        {
            TryLogFailure(exception);
            if (!silent)
            {
                MessageBox.Show(
                    $"操作未完成：{exception.Message}",
                    "Piko Setup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            return 1;
        }
    }

    private static void TryLogFailure(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(InstallerLayout.UserDataDirectory);
            File.AppendAllText(
                Path.Combine(InstallerLayout.UserDataDirectory, "setup.log"),
                $"[{DateTimeOffset.Now:O}] {exception}\r\n");
        }
        catch
        {
            // Installation diagnostics must never hide the original setup failure.
        }
    }

    private static void StartUninstallWorker(bool purgeData, bool silent)
    {
        var current = Environment.ProcessPath
                      ?? throw new InvalidOperationException("The setup executable path is unavailable.");
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "PikoDesktopPet",
            "uninstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var worker = Path.Combine(temporaryRoot, "Piko.Setup.exe");
        File.Copy(current, worker, overwrite: false);

        var arguments = new List<string> { "--uninstall-worker" };
        if (purgeData)
        {
            arguments.Add("--purge-data");
        }

        if (silent)
        {
            arguments.Add("--silent");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = worker,
            Arguments = string.Join(' ', arguments),
            WorkingDirectory = temporaryRoot,
            UseShellExecute = true
        })?.Dispose();
    }

    private static void WaitForRequestedProcess(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], "--wait-for-pid", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(args[index + 1], out var processId) || processId <= 0 || processId == Environment.ProcessId)
            {
                throw new ArgumentException("The update wait process ID is invalid.");
            }

            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.WaitForExit(30_000))
                {
                    throw new TimeoutException("Piko did not exit before the update timeout.");
                }
            }
            catch (ArgumentException)
            {
                // The process already exited before Setup opened it.
            }

            return;
        }
    }

    private static void ScheduleTemporaryUninstallerCleanup()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) ||
            !executable.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var directory = Path.GetDirectoryName(executable);
        MoveFileEx(executable, null, MoveFileDelayUntilReboot);
        if (directory is not null)
        {
            MoveFileEx(directory, null, MoveFileDelayUntilReboot);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, uint flags);
}
