using System.Security.Cryptography;
using Piko.Context.Events;
using Piko.Memory;
using Piko.Memory.Security;
using Piko.Runtime.Ipc;

namespace Piko.Runtime.Tests;

public sealed class MemoryIpcTests
{
    [Fact]
    public async Task AuthorizedBuildResultIsEncryptedRememberedListedAndDeleted()
    {
        var pipeName = $"PikoDesktopPet.Runtime.Test.{Guid.NewGuid():N}";
        var root = Path.Combine(Path.GetTempPath(), "PikoMemoryIpcTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        RuntimeUserSettingsFile.Save(
            Path.Combine(root, "runtime-settings.json"),
            new RuntimeUserSettings
            {
                MemoryEnabled = true,
                DevelopmentAwarenessEnabled = true,
                ProviderMode = AiProviderMode.LocalCompatible
            });
        var key = RandomNumberGenerator.GetBytes(32);
        var memoryStore = new SqliteMemoryStore(
            Path.Combine(root, "memory.db"),
            new AesGcmMemoryProtector(key));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var provider = new CapturingProvider();
        var host = new PikoRuntimeHost(
            new RuntimePaths(root),
            pipeName: pipeName,
            aiProvider: provider,
            memoryStore: memoryStore);
        var hostTask = host.RunAsync(timeout.Token);
        var client = new RuntimeIpcClient(pipeName, TimeSpan.FromSeconds(5));

        try
        {
            var published = await client.PublishContextEventAsync(
                new RuntimeContextEventEnvelope(
                    RuntimeContextEventEnvelope.CurrentSchemaVersion,
                    ContextEventTypes.BuildCompleted,
                    "vscode.extension",
                    DateTimeOffset.UtcNow,
                    Guid.NewGuid().ToString("N"),
                    ContextCapability.DevelopmentActivity.ToString(),
                    new Dictionary<string, RuntimeContextDataField>
                    {
                        ["success"] = new("false")
                    }),
                timeout.Token);
            Assert.True(published.Accepted);

            var plan = await client.PlanAgentAsync("上次构建失败了吗", timeout.Token);
            Assert.True(plan.Available);
            Assert.Contains("Build failed", provider.LastRequest!.SanitizedContext, StringComparison.Ordinal);
            Assert.Contains("untrusted historical evidence", provider.LastRequest.SystemInstruction, StringComparison.Ordinal);

            var memories = await client.ListMemoriesAsync(timeout.Token);
            Assert.True(memories.Available);
            var memory = Assert.Single(memories.Items);
            Assert.Equal("Episodic", memory.Kind);
            Assert.Equal("构建失败 Build failed", memory.Summary);

            Assert.Equal(1, await client.DeleteAllMemoriesAsync(timeout.Token));
            Assert.Empty((await client.ListMemoriesAsync(timeout.Token)).Items);
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

    private sealed class CapturingProvider : Piko.Agent.Models.IAiProvider
    {
        public Piko.Agent.Models.AiModelRequest? LastRequest { get; private set; }

        public ValueTask<Piko.Agent.Models.AiModelResponse> CompleteAsync(
            Piko.Agent.Models.AiModelRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return ValueTask.FromResult(new Piko.Agent.Models.AiModelResponse(
                true,
                "{\"message\":\"上次构建失败，我可以帮你复查。\",\"emotion\":\"concerned\",\"action\":\"concern\",\"toolCalls\":[]}",
                "test",
                "test"));
        }
    }
}
