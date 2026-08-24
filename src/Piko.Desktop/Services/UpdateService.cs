using System.Diagnostics;
using Piko.Update;

namespace Piko.Desktop.Services;

internal sealed class UpdateService
{
    private readonly string _updateDirectory;
    private readonly AppLogger _logger;
    private readonly UpdateClient _client;

    internal UpdateService(string dataRoot, AppLogger logger, UpdateClient? client = null)
    {
        _updateDirectory = Path.GetFullPath(Path.Combine(dataRoot, "updates"));
        _logger = logger;
        _client = client ?? new UpdateClient();
    }

    internal bool CanInstallAutomatically(ReleaseManifest manifest) =>
        manifest.Installer.AuthenticodeRequired && TrustedUpdateSigners.Thumbprints.Count > 0;

    internal Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
        _client.CheckAsync(PikoProductInfo.Version, cancellationToken);

    internal async Task<bool> DownloadVerifyAndStartAsync(
        ReleaseManifest manifest,
        CancellationToken cancellationToken = default)
    {
        if (!CanInstallAutomatically(manifest))
        {
            return false;
        }

        Directory.CreateDirectory(_updateDirectory);
        var safeVersion = manifest.Version.Replace('-', '_').Replace('.', '_');
        var partialPath = EnsureInsideUpdateDirectory(Path.Combine(_updateDirectory, $"Piko-{safeVersion}-Setup.download"));
        var installerPath = EnsureInsideUpdateDirectory(Path.Combine(_updateDirectory, $"Piko-{safeVersion}-Setup.exe"));
        if (File.Exists(partialPath)) File.Delete(partialPath);
        if (File.Exists(installerPath)) File.Delete(installerPath);

        try
        {
            await _client.DownloadInstallerAsync(manifest.Installer, partialPath, cancellationToken)
                .ConfigureAwait(false);
            var verification = UpdatePackageVerifier.Verify(
                partialPath,
                manifest.Installer,
                TrustedUpdateSigners.Thumbprints);
            if (!verification.IsTrusted)
            {
                _logger.Info($"Update package was rejected ({verification.Reason})");
                File.Delete(partialPath);
                return false;
            }

            File.Move(partialPath, installerPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = $"--silent --wait-for-pid {Environment.ProcessId}",
                WorkingDirectory = _updateDirectory,
                UseShellExecute = true
            })?.Dispose();
            _logger.Info($"Trusted update {manifest.Version} handed to Piko Setup");
            return true;
        }
        catch
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
            throw;
        }
    }

    private string EnsureInsideUpdateDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(
                _updateDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to write an update outside the Piko update directory.");
        }

        return full;
    }
}
