using System.Text.Json;
using System.Text.Json.Serialization;
using Piko.Context.Events;
using Piko.Context.Privacy;

namespace Piko.Runtime;

public sealed record RuntimeUserSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public bool DevelopmentAwarenessEnabled { get; init; }
    public bool GitAwarenessEnabled { get; init; }
    public bool AgentReadEnabled { get; init; }
    public bool CloudAiEnabled { get; init; }
    public AiProviderMode ProviderMode { get; init; } = AiProviderMode.Disabled;
    public bool MemoryEnabled { get; init; }
    public string AiEndpoint { get; init; } = "https://api.openai.com/v1/";
    public string AiModel { get; init; } = "gpt-5.4";
    public string LocalAiEndpoint { get; init; } = "http://127.0.0.1:11434/v1/";
    public string LocalAiModel { get; init; } = "local-model";
    public UserAddressMode UserAddressMode { get; init; } = UserAddressMode.Master;
    public string UserName { get; init; } = string.Empty;
    public string CustomAddress { get; init; } = string.Empty;
    public string Personality { get; init; } = "温暖、简短、稍微活泼";
    public PetProactivity Proactivity { get; init; } = PetProactivity.Low;
    public bool ForegroundActivityAwarenessEnabled { get; init; } = true;
    public bool FileActivityAwarenessEnabled { get; init; } = true;

    [JsonIgnore]
    public AiProviderMode EffectiveAiProviderMode =>
        ProviderMode == AiProviderMode.Disabled && CloudAiEnabled
            ? AiProviderMode.OpenAiApi
            : ProviderMode;

    public string ResolveUserAddress() => UserAddressMode switch
    {
        UserAddressMode.Name when !string.IsNullOrWhiteSpace(UserName) => UserName.Trim(),
        UserAddressMode.Custom when !string.IsNullOrWhiteSpace(CustomAddress) => CustomAddress.Trim(),
        _ => "主人"
    };

    public RuntimeUserSettings Validate()
    {
        ValidateEndpoint(AiEndpoint, nameof(AiEndpoint), requireLoopback: false);
        ValidateEndpoint(LocalAiEndpoint, nameof(LocalAiEndpoint), requireLoopback: true);
        ValidateModel(AiModel, nameof(AiModel));
        ValidateModel(LocalAiModel, nameof(LocalAiModel));
        ValidateText(UserName, 80, nameof(UserName));
        ValidateText(CustomAddress, 40, nameof(CustomAddress));
        ValidateText(Personality, 240, nameof(Personality), required: true);

        return this;
    }

    public PrivacyProfile ToPrivacyProfile()
    {
        var profile = PrivacyProfile.LocalFirst();
        if (!ForegroundActivityAwarenessEnabled)
        {
            profile = profile.WithGrant(ContextCapability.ForegroundApplicationCategory, PermissionGrant.Denied);
        }

        if (!FileActivityAwarenessEnabled)
        {
            profile = profile.WithGrant(ContextCapability.FileActivity, PermissionGrant.Denied);
        }

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

        if (EffectiveAiProviderMode == AiProviderMode.OpenAiApi)
        {
            profile = profile.WithGrant(ContextCapability.CloudAiProcessing, PermissionGrant.AllowAlways);
        }

        return profile;
    }

    private static void ValidateEndpoint(string value, string parameterName, bool requireLoopback)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            (requireLoopback && !endpoint.IsLoopback) ||
            (!endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !(endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && endpoint.IsLoopback)) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException(requireLoopback
                ? "Local AI endpoint must be an explicit loopback HTTP/HTTPS address."
                : "AI endpoint must use HTTPS, except for an explicit loopback provider.", parameterName);
        }
    }

    private static void ValidateModel(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
        {
            throw new ArgumentException("AI model identifier is invalid.", parameterName);
        }
    }

    private static void ValidateText(string value, int maximumLength, string parameterName, bool required = false)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) || value.Length > maximumLength || value.Any(char.IsControl))
        {
            throw new ArgumentException("Pet profile text is invalid.", parameterName);
        }
    }
}

public static class RuntimeUserSettingsFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
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
            if (settings is null)
            {
                return new RuntimeUserSettings();
            }

            return settings.SchemaVersion switch
            {
                RuntimeUserSettings.CurrentSchemaVersion => settings.Validate(),
                1 => (settings with
                {
                    SchemaVersion = RuntimeUserSettings.CurrentSchemaVersion,
                    ProviderMode = settings.CloudAiEnabled
                        ? AiProviderMode.OpenAiApi
                        : AiProviderMode.Disabled
                }).Validate(),
                _ => new RuntimeUserSettings()
            };
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

