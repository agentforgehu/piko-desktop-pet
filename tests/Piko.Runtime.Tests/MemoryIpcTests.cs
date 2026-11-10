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

            var profileValidUntil = DateTimeOffset.UtcNow.AddDays(60);
            await client.AddMemoryAsync("Profile", "偏好使用简洁的中文回答", timeout.Token, profileValidUntil);

            var memories = await client.ListMemoriesAsync(timeout.Token);
            Assert.True(memories.Available);
            Assert.Equal(2, memories.Items.Count);
            var profile = Assert.Single(memories.Items, item => item.Kind == "Profile");
            Assert.Equal("偏好使用简洁的中文回答", profile.Summary);
            Assert.Equal("UserConfirmed", profile.Provenance);
            Assert.NotNull(profile.ValidFrom);
            Assert.Equal(profileValidUntil, profile.ValidUntil);
            var related = await client.FindRelatedMemoriesAsync("Profile", "希望继续使用简体中文回答", timeout.Token);
            Assert.True(related.Available);
            Assert.Equal(profile.Id, Assert.Single(related.Items).Id);
            Assert.True(await client.UpdateMemoryAsync(
                profile.Id, profile.UpdatedAt, "Profile", "偏好使用简体中文和简洁回答", timeout.Token, profileValidUntil));
            Assert.False(await client.UpdateMemoryAsync(
                profile.Id, profile.UpdatedAt, "Profile", "过时编辑不应覆盖新值", timeout.Token));
            profile = Assert.Single((await client.ListMemoriesAsync(timeout.Token)).Items, item => item.Kind == "Profile");
            Assert.Equal("偏好使用简体中文和简洁回答", profile.Summary);
            var priorProfileId = profile.Id;
            var replacementValidUntil = DateTimeOffset.UtcNow.AddDays(90);
            Assert.True(await client.ReplaceMemoryAsync(
                profile.Id, profile.UpdatedAt, "Profile", "偏好简体中文并先给结论", timeout.Token, replacementValidUntil));
            var profileVersions = (await client.ListMemoriesAsync(timeout.Token)).Items
                .Where(item => item.Kind == "Profile").ToArray();
            Assert.Equal(2, profileVersions.Length);
            Assert.Equal("偏好简体中文并先给结论", Assert.Single(profileVersions, item => !item.IsSuperseded).Summary);
            Assert.Equal(priorProfileId, Assert.Single(profileVersions, item => item.IsSuperseded).Id);
            Assert.Equal(replacementValidUntil, Assert.Single(profileVersions, item => !item.IsSuperseded).ValidUntil);
            Assert.True(Assert.Single(profileVersions, item => item.IsSuperseded).ValidUntil <= DateTimeOffset.UtcNow);
            profile = Assert.Single(profileVersions, item => !item.IsSuperseded);
            related = await client.FindRelatedMemoriesAsync("Profile", "偏好简体中文并先给结论", timeout.Token);
            Assert.Equal(profile.Id, Assert.Single(related.Items).Id);
            Assert.True(await client.DeleteMemoryAsync(profile.Id, timeout.Token));
            Assert.False(await client.DeleteMemoryAsync(profile.Id, timeout.Token));
            var remaining = (await client.ListMemoriesAsync(timeout.Token)).Items;
            Assert.Equal(2, remaining.Count);
            Assert.Contains(remaining, item => item.Kind == "Episodic" && item.Summary == "构建失败 Build failed");
            Assert.Contains(remaining, item => item.Id == priorProfileId && item.IsSuperseded);

            var validFrom = DateTimeOffset.UtcNow.AddDays(2);
            var validUntil = validFrom.AddDays(7);
            await client.AddMemoryAsync("Semantic", "下周开始切换到蓝色主题", timeout.Token, validUntil, validFrom);
            var scheduled = Assert.Single((await client.ListMemoriesAsync(timeout.Token)).Items,
                item => item.Kind == "Semantic");
            Assert.Equal(validFrom, scheduled.ValidFrom);
            Assert.Equal(validUntil, scheduled.ValidUntil);
            await client.PlanAgentAsync("下周开始切换到蓝色主题", timeout.Token);
            Assert.DoesNotContain("下周开始切换到蓝色主题", provider.LastRequest!.SanitizedContext, StringComparison.Ordinal);

            Assert.Equal(3, await client.DeleteAllMemoriesAsync(timeout.Token));
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
