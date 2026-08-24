using Piko.Context.Events;
using Piko.Context.Privacy;

namespace Piko.Context.Tests;

public sealed class ContextPrivacyFilterTests
{
    [Fact]
    public void LocalFirst_AllowsApplicationCategoryButRemovesHighFields()
    {
        var contextEvent = ContextEvent.Create(
            ContextEventTypes.ForegroundApplicationChanged,
            "windows",
            DateTimeOffset.UnixEpoch,
            "session",
            ContextCapability.ForegroundApplicationCategory,
            data: new Dictionary<string, ContextDataValue>
            {
                ["category"] = new("development", DataSensitivity.Low),
                ["windowTitle"] = new("private project title", DataSensitivity.High)
            });
        var filter = new ContextPrivacyFilter(PrivacyProfile.LocalFirst());

        var decision = filter.Apply(contextEvent, PrivacyDestination.LocalRetention);

        Assert.True(decision.Allowed);
        Assert.NotNull(decision.Event);
        Assert.Equal(1, decision.RemovedFieldCount);
        Assert.True(decision.Event.Data.ContainsKey("category"));
        Assert.False(decision.Event.Data.ContainsKey("windowTitle"));
    }

    [Fact]
    public void LocalFirst_DeniesWindowTitlesAndCloudProcessing()
    {
        var titleEvent = ContextEvent.Create(
            ContextEventTypes.ForegroundApplicationChanged,
            "windows",
            DateTimeOffset.UnixEpoch,
            "session",
            ContextCapability.WindowTitle);
        var categoryEvent = ContextEvent.Create(
            ContextEventTypes.ForegroundApplicationChanged,
            "windows",
            DateTimeOffset.UnixEpoch,
            "session",
            ContextCapability.ForegroundApplicationCategory);
        var filter = new ContextPrivacyFilter(PrivacyProfile.LocalFirst());

        Assert.False(filter.Apply(titleEvent, PrivacyDestination.LiveProcessing).Allowed);
        Assert.False(filter.Apply(categoryEvent, PrivacyDestination.CloudAi).Allowed);
    }

    [Fact]
    public void SessionGrant_DowngradesPersistentRetention()
    {
        var profile = PrivacyProfile.LocalFirst()
            .WithGrant(ContextCapability.DiagnosticsSummary, PermissionGrant.AllowSession);
        var contextEvent = ContextEvent.Create(
            ContextEventTypes.DiagnosticsChanged,
            "vscode",
            DateTimeOffset.UnixEpoch,
            "session",
            ContextCapability.DiagnosticsSummary,
            retention: RetentionClass.Persistent);

        var decision = new ContextPrivacyFilter(profile)
            .Apply(contextEvent, PrivacyDestination.LocalRetention);

        Assert.True(decision.Allowed);
        Assert.Equal(RetentionClass.Session, decision.Event!.Retention);
    }
}
