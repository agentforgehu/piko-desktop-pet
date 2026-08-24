using System.Text.Json;
using Piko.Context.Events;
using Piko.Context.Privacy;

namespace Piko.Runtime;

public sealed record RuntimeUserSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public bool DevelopmentAwarenessEnabled { get; init; }
    public bool GitAwarenessEnabled { get; init; }
    public bool AgentReadEnabled { get; init; }
    public bool CloudAiEnabled { get; init; }
    public bool MemoryEnabled { get; init; }
    public string AiEndpoint { get; init; } = "https://api.openai.com/v1/";
    public string AiModel { get; init; } = "gpt-5.4";

    public RuntimeUserSettings Validate()
    {
        if (!Uri.TryCreate(AiEndpoint, UriKind.Absolute, out var endpoint) ||
            (!endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !(endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && endpoint.IsLoopback)) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException("AI endpoint must use HTTPS, except for a loopback provider.", nameof(AiEndpoint));
        }

        if (string.IsNullOrWhiteSpace(AiModel) || AiModel.Length > 128 || AiModel.Any(char.IsControl))
        {
            throw new ArgumentException("AI model identifier is invalid.", nameof(AiModel));
        }

        return this;
    }

    public PrivacyProfile ToPrivacyProfile()
    {
        var profile = PrivacyProfile.LocalFirst();
        if (DevelopmentAwarenessEnabled)
        {
            profile = profile
                .WithGrant(ContextCapability.ProjectIdentity, PermissionGrant.AllowAlways)
                .WithGrant(ContextCapability.DevelopmentActivity, PermissionGrant.AllowAlways)
                .WithGrant(ContextCapability.DiagnosticsSummary, PermissionGrant.AllowAlways)
                .WithGrant(ContextCapability.TerminalSummary, PermissionGrant.AllowSession);
        }

        if (GitAwarenessEnabled)
        {
            profile = profile.WithGrant(ContextCapability.GitMetadata, PermissionGrant.AllowAlways);
        }

        if (AgentReadEnabled)
        {
            profile = profile.WithGrant(ContextCapability.AgentRead, PermissionGrant.AllowAlways);
        }

        if (CloudAiEnabled)
        {
            profile = profile.WithGrant(ContextCapability.CloudAiProcessing, PermissionGrant.AllowAlways);
        }

        return profile;
    }
}

public static class RuntimeUserSettingsFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static RuntimeUserSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new RuntimeUserSettings();
            }

            var settings = JsonSerializer.Deserialize<RuntimeUserSettings>(
                File.ReadAllText(path),
                JsonOptions);
            return settings?.SchemaVersion == RuntimeUserSettings.CurrentSchemaVersion
                ? settings.Validate()
                : new RuntimeUserSettings();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new RuntimeUserSettings();
        }
    }

    public static void Save(string path, RuntimeUserSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SchemaVersion != RuntimeUserSettings.CurrentSchemaVersion)
        {
            throw new ArgumentException("Unsupported runtime settings schema.", nameof(settings));
        }
        settings.Validate();

        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException("Settings path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, path, true);
    }
}
