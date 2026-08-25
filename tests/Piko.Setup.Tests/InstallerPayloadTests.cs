using System.IO.Compression;
using Xunit;

namespace Piko.Setup.Tests;

public sealed class InstallerPayloadTests
{
    [Fact]
    public void ValidNestedPayloadExtractsRequiredExecutables()
    {
        using var payload = CreateZip(
            ("Piko-1.0/Piko.exe", "desktop"),
            ("Piko-1.0/Piko.Runtime.exe", "runtime"),
            ("Piko-1.0/readme.txt", "docs"));
        var root = CreateTemporaryDirectory();
        try
        {
            InstallerPayload.Validate(payload);
            payload.Position = 0;
            var applicationDirectory = InstallerPayload.Extract(payload, root);

            Assert.Equal("desktop", File.ReadAllText(Path.Combine(applicationDirectory, "Piko.exe")));
            Assert.Equal("runtime", File.ReadAllText(Path.Combine(applicationDirectory, "Piko.Runtime.exe")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TraversalEntryIsRejectedWithoutEscapingDestination()
    {
        using var payload = CreateZip(
            ("Piko.exe", "desktop"),
            ("Piko.Runtime.exe", "runtime"),
            ("../escaped.txt", "blocked"));
        var root = CreateTemporaryDirectory();
        var escaped = Path.Combine(Path.GetDirectoryName(root)!, "escaped.txt");
        try
        {
            Assert.Throws<InvalidDataException>(() => InstallerPayload.Extract(payload, root));
            Assert.False(File.Exists(escaped));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PayloadMustContainBothRuntimeAndDesktop()
    {
        using var payload = CreateZip(("Piko.exe", "desktop"));
        Assert.Throws<InvalidDataException>(() => InstallerPayload.Validate(payload));
    }

    [Fact]
    public void StartMenuShortcutInteropCreatesARealLink()
    {
        var root = CreateTemporaryDirectory();
        var shortcut = Path.Combine(root, "Piko.lnk");
        try
        {
            ShortcutService.Create(shortcut, Environment.ProcessPath!, root);

            Assert.True(File.Exists(shortcut));
            Assert.True(new FileInfo(shortcut).Length > 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ManagedDeleteRefusesPathsOutsideInstallRoot()
    {
        var outside = CreateTemporaryDirectory();
        try
        {
            Assert.Throws<InvalidOperationException>(() => InstallerLayout.DeleteManagedDirectory(outside));
            Assert.True(Directory.Exists(outside));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void ManagedApplicationPathRecognizesLegacyInstallWithoutTrustingSiblings()
    {
        var legacyExecutable = Path.Combine(
            InstallerLayout.LegacyInstallRoot,
            "Piko-0.1.0-win-x64",
            "Piko.exe");
        var unrelatedExecutable = Path.Combine(
            InstallerLayout.ProgramsRoot,
            "Piko Desktop Pet Backup",
            "Piko.exe");

        Assert.True(InstallerLayout.IsManagedApplicationPath(legacyExecutable));
        Assert.False(InstallerLayout.IsManagedApplicationPath(unrelatedExecutable));
    }

    private static MemoryStream CreateZip(params (string Path, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(file.Content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Piko.Setup.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
