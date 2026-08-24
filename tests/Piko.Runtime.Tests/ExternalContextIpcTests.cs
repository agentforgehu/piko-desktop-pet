using Piko.Context.Events;
using Piko.Context.Situations;
using Piko.Context.Windows.Observation;
using Piko.Runtime.Ipc;

namespace Piko.Runtime.Tests;

public sealed class ExternalContextIpcTests
{
    [Fact]
    public async Task DevelopmentEventIsRejectedUntilUserEnablesSummaryPermission()
    {
        var result = await RunWithSettingsAsync(
            new RuntimeUserSettings(),
            CreateEvent(ContextEventTypes.BuildStarted, ContextCapability.DevelopmentActivity));

        Assert.False(result.Accepted);
        Assert.Equal("capability_denied", result.Reason);
    }

    [Fact]
    public async Task EnabledDevelopmentSummaryCanDriveBuildingSituation()
    {
        var result = await RunWithSettingsAsync(
            new RuntimeUserSettings { DevelopmentAwarenessEnabled = true },
            CreateEvent(ContextEventTypes.BuildStarted, ContextCapability.DevelopmentActivity));

        Assert.True(result.Accepted);
        Assert.Equal("Building", result.Situation);
    }

    [Fact]
    public async Task RuntimeRejectsCapabilityConfusionFromExternalSource()
    {
        var pipeName = $"PikoDesktopPet.Runtime.Test.{Guid.NewGuid():N}";
        var root = CreateRoot();
        RuntimeUserSettingsFile.Save(
            Path.Combine(root, "runtime-settings.json"),
            new RuntimeUserSettings
            {
                DevelopmentAwarenessEnabled = true,
                GitAwarenessEnabled = true
            });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var host = new PikoRuntimeHost(
            new RuntimePaths(root),
            new ActiveWindowsContextProbe(),
            pipeName);
        var hostTask = host.RunAsync(timeout.Token);
        var client = new RuntimeIpcClient(pipeName, TimeSpan.FromSeconds(5));

        try
        {
            var invalid = CreateEvent(
                ContextEventTypes.BuildStarted,
                ContextCapability.GitMetadata);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.PublishContextEventAsync(invalid, timeout.Token));

            Assert.Equal("external_event_capability_denied", error.Message);
            await client.StopAsync(timeout.Token);
            await hostTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            timeout.Cancel();
            await hostTask.WaitAsync(TimeSpan.FromSeconds(5));
            Directory.Delete(root, true);
        }
    }

    private static async Task<RuntimeContextPublishResult> RunWithSettingsAsync(
        RuntimeUserSettings settings,
        RuntimeContextEventEnvelope contextEvent)
    {
        var pipeName = $"PikoDesktopPet.Runtime.Test.{Guid.NewGuid():N}";
        var root = CreateRoot();
        RuntimeUserSettingsFile.Save(Path.Combine(root, "runtime-settings.json"), settings);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var host = new PikoRuntimeHost(
            new RuntimePaths(root),
            new ActiveWindowsContextProbe(),
            pipeName);
        var hostTask = host.RunAsync(timeout.Token);
        var client = new RuntimeIpcClient(pipeName, TimeSpan.FromSeconds(5));

        try
        {
            var result = await client.PublishContextEventAsync(contextEvent, timeout.Token);
            await client.StopAsync(timeout.Token);
            await hostTask.WaitAsync(TimeSpan.FromSeconds(5));
            return result;
        }
        finally
        {
            timeout.Cancel();
            await hostTask.WaitAsync(TimeSpan.FromSeconds(5));
            Directory.Delete(root, true);
        }
    }

    private static RuntimeContextEventEnvelope CreateEvent(
        string type,
        ContextCapability capability) => new(
        RuntimeContextEventEnvelope.CurrentSchemaVersion,
        type,
        "vscode.extension",
        DateTimeOffset.UtcNow,
        Guid.NewGuid().ToString("N"),
        capability.ToString());

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PikoExternalContextTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class ActiveWindowsContextProbe : IWindowsContextProbe
    {
        public WindowsContextSnapshot Capture(int idleThresholdSeconds = 120) => new(
            DateTimeOffset.UtcNow,
            PresenceState.Active,
            0,
            ApplicationCategory.Unknown,
            false);
    }
}
