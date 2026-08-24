using Piko.Context.Events;
using Piko.Context.Privacy;

namespace Piko.Runtime.Tests;

public sealed class RuntimeUserSettingsTests
{
    [Fact]
    public void DefaultsDenyDevelopmentGitCloudAndAgentCapabilities()
    {
        var profile = new RuntimeUserSettings().ToPrivacyProfile();

        Assert.Equal(PermissionGrant.Denied, profile.GrantFor(ContextCapability.DiagnosticsSummary));
        Assert.Equal(PermissionGrant.Denied, profile.GrantFor(ContextCapability.GitMetadata));
        Assert.Equal(PermissionGrant.Denied, profile.GrantFor(ContextCapability.CloudAiProcessing));
        Assert.Equal(PermissionGrant.Denied, profile.GrantFor(ContextCapability.AgentRead));
        Assert.Equal(PermissionGrant.Denied, profile.GrantFor(ContextCapability.AgentWrite));
    }

    [Fact]
    public void ExplicitSettingsGrantOnlySelectedSummaryCapabilities()
    {
        var profile = new RuntimeUserSettings
        {
            DevelopmentAwarenessEnabled = true,
            GitAwarenessEnabled = true,
            AgentReadEnabled = true
        }.ToPrivacyProfile();

        Assert.Equal(PermissionGrant.AllowAlways, profile.GrantFor(ContextCapability.DiagnosticsSummary));
        Assert.Equal(PermissionGrant.AllowAlways, profile.GrantFor(ContextCapability.DevelopmentActivity));
        Assert.Equal(PermissionGrant.AllowAlways, profile.GrantFor(ContextCapability.GitMetadata));
        Assert.Equal(PermissionGrant.AllowAlways, profile.GrantFor(ContextCapability.AgentRead));
        Assert.Equal(PermissionGrant.Denied, profile.GrantFor(ContextCapability.DiagnosticsDetails));
        Assert.Equal(PermissionGrant.Denied, profile.GrantFor(ContextCapability.TerminalOutput));
        Assert.Equal(PermissionGrant.Denied, profile.GrantFor(ContextCapability.AgentWrite));
    }

    [Fact]
    public void SettingsFileRoundTripsAtomically()
    {
        var root = Path.Combine(Path.GetTempPath(), "PikoRuntimeSettingsTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "runtime-settings.json");
        var expected = new RuntimeUserSettings
        {
            DevelopmentAwarenessEnabled = true,
            GitAwarenessEnabled = true
        };

        try
        {
            RuntimeUserSettingsFile.Save(path, expected);

            Assert.Equal(expected, RuntimeUserSettingsFile.Load(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
