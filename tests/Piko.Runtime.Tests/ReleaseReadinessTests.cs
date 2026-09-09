using Piko.Desktop.Services;

namespace Piko.Runtime.Tests;

public sealed class ReleaseReadinessTests
{
    [Fact]
    public void FreshInstallShowsWelcomeWithoutEnablingOptionalCapabilities()
    {
        var settings = new PikoSettings().UpgradeOrDefault();
        Assert.False(settings.HasCompletedWelcome);
        Assert.Equal(AiProviderMode.Disabled, settings.ProviderMode);
        Assert.False(settings.AgentReadEnabled);
        Assert.False(settings.MemoryEnabled);
        Assert.False(settings.LaunchAtStartup);
        Assert.False(settings.DevelopmentAwarenessEnabled);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExistingUsersKeepPreferencesAndSkipFirstRunGuide(int schema)
    {
        var settings = new PikoSettings
        {
            SchemaVersion = schema, AutonomousBehaviorEnabled = false,
            MemoryEnabled = true, UserName = "Piko user", CloudAiEnabled = true
        }.UpgradeOrDefault();
        Assert.True(settings.HasCompletedWelcome);
        Assert.False(settings.AutonomousBehaviorEnabled);
        Assert.True(settings.MemoryEnabled);
        Assert.Equal("Piko user", settings.UserName);
        Assert.Equal(PikoSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(schema < 3 ? AiProviderMode.OpenAiApi : AiProviderMode.Disabled, settings.ProviderMode);
    }

    [Fact]
    public void CurrentSchemaPreservesLocalProviderAndCompletedWelcome()
    {
        var settings = new PikoSettings { ProviderMode = AiProviderMode.LocalCompatible, HasCompletedWelcome = true };
        Assert.Equal(settings, settings.UpgradeOrDefault());
    }

    [Fact]
    public void UnknownOptionsAreRejectedBeforeSavingSettings()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeUserSettings { ProviderMode = (AiProviderMode)999 }.Validate());
        Assert.Throws<ArgumentException>(() => new RuntimeUserSettings { Proactivity = (PetProactivity)999 }.Validate());
        Assert.Throws<ArgumentException>(() => new RuntimeUserSettings { UserAddressMode = (UserAddressMode)999 }.Validate());
    }
    [Theory]
    [InlineData("{\"schemaVersion\":4,\"providerMode\":999}", "{\"schemaVersion\":2,\"providerMode\":999}")]
    [InlineData("{\"schemaVersion\":4,\"personality\":null}", "{\"schemaVersion\":2,\"personality\":null}")]
    public void InvalidPersistedSettingsRecoverWithoutBreakingStartup(string desktopJson, string runtimeJson)
    {
        var directory = Path.Combine(Path.GetTempPath(), "piko-settings-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(directory);
            File.WriteAllText(paths.SettingsFile, desktopJson);
            File.WriteAllText(paths.RuntimeSettingsFile, runtimeJson);
            Assert.Equal(new PikoSettings(), new SettingsStore(paths).Load());
            Assert.Equal(new RuntimeUserSettings(), RuntimeUserSettingsFile.Load(paths.RuntimeSettingsFile));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
