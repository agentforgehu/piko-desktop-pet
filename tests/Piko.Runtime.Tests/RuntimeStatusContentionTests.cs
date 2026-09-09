using Piko.Context.Situations;
using Piko.Context.Windows.Observation;
using Piko.Runtime.Ipc;

namespace Piko.Runtime.Tests;

public sealed class RuntimeStatusContentionTests
{
    [Fact]
    public void DiagnosticReaderCannotBreakStatusPersistenceAndNextWriteRecovers()
    {
        var root = Path.Combine(Path.GetTempPath(), "piko-status-contention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "status.json");
            var store = new RuntimeStatusStore(path);
            var first = RuntimeStatusSnapshot.Starting(DateTimeOffset.UtcNow);
            store.Save(first);
            var next = first with { Health = "healthy", LastHeartbeatAt = first.LastHeartbeatAt.AddSeconds(1) };
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // Reproduce the Windows file-replacement sharing violation from external readers.
                Assert.Throws<IOException>(() => store.Save(next));
                Assert.False(store.TrySave(next));
                Assert.Equal(first, store.Load());
            }
            Assert.True(store.TrySave(next));
            Assert.Equal(next, store.Load());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RuntimeKeepsServingHealthWhileStatusFileIsLockedAndRecoversAfterRelease()
    {
        var root = Path.Combine(Path.GetTempPath(), "piko-host-contention-" + Guid.NewGuid().ToString("N"));
        var paths = new RuntimePaths(root);
        var store = new RuntimeStatusStore(paths.StatusFile);
        store.Save(RuntimeStatusSnapshot.Starting(DateTimeOffset.UtcNow));
        var reader = new FileStream(paths.StatusFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipe = "PikoContention." + Guid.NewGuid().ToString("N");
        var host = new PikoRuntimeHost(paths, new ActiveProbe(), pipe).RunAsync(shutdown.Token);
        var client = new RuntimeIpcClient(pipe, TimeSpan.FromSeconds(3));
        try
        {
            var readyDeadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while ((await client.GetHealthAsync(shutdown.Token)).Health != "healthy" && DateTimeOffset.UtcNow < readyDeadline)
                await Task.Delay(50, shutdown.Token);
            Assert.Equal("healthy", (await client.GetHealthAsync(shutdown.Token)).Health);
            await Task.Delay(1200, shutdown.Token); // Include another periodic write while locked.
            Assert.False(host.IsCompleted);
            Assert.Equal("healthy", (await client.GetHealthAsync(shutdown.Token)).Health);
            reader.Dispose();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(4);
            while (store.Load()?.Health != "healthy" && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(100, shutdown.Token);
            Assert.Equal("healthy", store.Load()?.Health);
            await client.StopAsync(shutdown.Token);
            await host.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            reader.Dispose();
            shutdown.Cancel();
            try { await host.WaitAsync(TimeSpan.FromSeconds(3)); }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    private sealed class ActiveProbe : IWindowsContextProbe
    {
        public WindowsContextSnapshot Capture(int idleThresholdSeconds = 120) => new(
            DateTimeOffset.UtcNow, PresenceState.Active, 0, ApplicationCategory.Unknown, false);
    }
}
