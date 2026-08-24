using Piko.Runtime.Ipc;

namespace Piko.Runtime.Tests;

public sealed class RuntimeIpcTests
{
    [Fact]
    public async Task ClientAndServerExchangeTypedHealthOverCurrentUserPipe()
    {
        var pipeName = $"PikoDesktopPet.Runtime.Test.{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var expected = RuntimeStatusSnapshot.Starting(now);
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = new RuntimeIpcServer(
            request => RuntimeResponse.Ok(request.RequestId, "health", expected),
            pipeName);
        var serverTask = server.RunAsync(shutdown.Token);

        var client = new RuntimeIpcClient(pipeName, TimeSpan.FromSeconds(5));
        var actual = await client.GetHealthAsync(shutdown.Token);

        Assert.Equal(expected, actual);
        shutdown.Cancel();
        await serverTask;
    }

    [Fact]
    public async Task ClientRejectsResponseWithWrongRequestId()
    {
        var pipeName = $"PikoDesktopPet.Runtime.Test.{Guid.NewGuid():N}";
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = new RuntimeIpcServer(
            _ => RuntimeResponse.Ok("wrong", "health", RuntimeStatusSnapshot.Starting(DateTimeOffset.UtcNow)),
            pipeName);
        var serverTask = server.RunAsync(shutdown.Token);
        var client = new RuntimeIpcClient(pipeName, TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetHealthAsync(shutdown.Token));

        shutdown.Cancel();
        await serverTask;
    }

    [Fact]
    public async Task RuntimeHostStopsCleanlyThroughCurrentUserIpc()
    {
        var pipeName = $"PikoDesktopPet.Runtime.Test.{Guid.NewGuid():N}";
        var root = Path.Combine(Path.GetTempPath(), "PikoRuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var host = new PikoRuntimeHost(new RuntimePaths(root), pipeName: pipeName);
        var hostTask = host.RunAsync(timeout.Token);
        var client = new RuntimeIpcClient(pipeName, TimeSpan.FromSeconds(5));

        try
        {
            var status = await client.GetHealthAsync(timeout.Token);
            Assert.True(status.Health is "starting" or "healthy");

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
}
