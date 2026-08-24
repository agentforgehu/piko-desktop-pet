using System.Text.Json;
using System.Text.Json.Serialization;

namespace Piko.Update;

public sealed record UpdateInstaller(
    Uri Url,
    string Sha256,
    long SizeBytes,
    bool AuthenticodeRequired);

public sealed record ReleaseManifest(
    int SchemaVersion,
    string Version,
    string Channel,
    DateTimeOffset PublishedAt,
    Uri ReleasePage,
    UpdateInstaller Installer)
{
    public const int CurrentSchemaVersion = 1;
    private const long MaximumInstallerBytes = 350L * 1024 * 1024;

    public SemanticVersion SemanticVersion => SemanticVersion.Parse(Version);

    public static ReleaseManifest Parse(ReadOnlySpan<byte> json)
    {
        if (json.Length is < 2 or > 64 * 1024)
        {
            throw new InvalidDataException("Update manifest size is invalid.");
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(json, options)
                       ?? throw new InvalidDataException("Update manifest is empty.");
        manifest.Validate();
        return manifest;
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException("Update manifest schema is unsupported.");
        }

        _ = SemanticVersion;
        if (Channel is not ("stable" or "preview"))
        {
            throw new InvalidDataException("Update channel is invalid.");
        }

        ValidateGitHubUri(ReleasePage, requireAssetPath: false);
        ValidateGitHubUri(Installer.Url, requireAssetPath: true);
        if (Installer.SizeBytes is < 1 or > MaximumInstallerBytes)
        {
            throw new InvalidDataException("Update installer size is invalid.");
        }

        if (Installer.Sha256.Length != 64 || Installer.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Update installer SHA-256 is invalid.");
        }
    }

    private static void ValidateGitHubUri(Uri uri, bool requireAssetPath)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException("Update URL is not an allowed GitHub HTTPS URL.");
        }

        const string repositoryPath = "/agentforgehu/piko-desktop-pet/";
        if (!uri.AbsolutePath.StartsWith(repositoryPath, StringComparison.OrdinalIgnoreCase) ||
            (requireAssetPath && !uri.AbsolutePath.Contains("/releases/download/", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Update URL is outside the official Piko repository.");
        }
    }
}
