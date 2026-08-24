using System.IO.Compression;
using System.Reflection;

namespace Piko.Setup;

internal static class InstallerPayload
{
    internal const string ResourceName = "Piko.Payload.zip";
    private const int MaximumEntries = 128;
    private const long MaximumEntryBytes = 220L * 1024 * 1024;
    private const long MaximumTotalBytes = 350L * 1024 * 1024;

    internal static Stream OpenEmbedded()
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
               ?? throw new InvalidOperationException("The installer payload is missing.");
    }

    internal static void Validate(Stream payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
        ValidateArchive(archive);
        var names = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToArray();
        if (!names.Any(name => name.EndsWith("/Piko.exe", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(name, "Piko.exe", StringComparison.OrdinalIgnoreCase)) ||
            !names.Any(name => name.EndsWith("/Piko.Runtime.exe", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(name, "Piko.Runtime.exe", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The installer payload does not contain the required Piko executables.");
        }
    }

    internal static string Extract(Stream payload, string destination)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var destinationRoot = Path.GetFullPath(destination)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Directory.CreateDirectory(destinationRoot);

        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
        ValidateArchive(archive);
        foreach (var entry in archive.Entries)
        {
            var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (relativePath.Contains(':'))
            {
                throw new InvalidDataException("The installer payload contains an invalid path.");
            }

            var target = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
            if (!target.StartsWith(
                    destinationRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The installer payload attempted to escape the staging directory.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            var parent = Path.GetDirectoryName(target)
                         ?? throw new InvalidDataException("The installer payload contains an invalid file path.");
            Directory.CreateDirectory(parent);
            using var source = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(output);
        }

        return ResolveApplicationDirectory(destinationRoot);
    }

    internal static string ResolveApplicationDirectory(string extractionRoot)
    {
        var pikoExecutables = Directory.GetFiles(
            extractionRoot,
            "Piko.exe",
            SearchOption.AllDirectories);
        if (pikoExecutables.Length != 1)
        {
            throw new InvalidDataException("The installer payload must contain exactly one Piko.exe.");
        }

        var directory = Path.GetDirectoryName(pikoExecutables[0])
                        ?? throw new InvalidDataException("Piko.exe has no parent directory.");
        if (!File.Exists(Path.Combine(directory, "Piko.Runtime.exe")))
        {
            throw new InvalidDataException("Piko.Runtime.exe must be beside Piko.exe.");
        }

        return directory;
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count is < 1 or > MaximumEntries)
        {
            throw new InvalidDataException("The installer payload has an invalid entry count.");
        }

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length < 0 || entry.Length > MaximumEntryBytes)
            {
                throw new InvalidDataException("The installer payload contains an oversized entry.");
            }

            total = checked(total + entry.Length);
            if (total > MaximumTotalBytes)
            {
                throw new InvalidDataException("The installer payload exceeds the extraction limit.");
            }
        }
    }
}
