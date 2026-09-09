using System.IO.Pipes;
using Piko.Agent.Models;
using Piko.Runtime.Ipc;

namespace Piko.Runtime.Tests;

public sealed class RuntimeInteractionTests
{
    [Fact]
    public async Task SlowModelKeepsHealthResponsiveAndRejectsDuplicateWork()
    {
        await using var runtime = new TestRuntime();
        var request = runtime.Client.PlanAgentAsync("hello", runtime.Shutdown.Token);
        await runtime.Provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var health = await runtime.Client.GetHealthAsync(runtime.Shutdown.Token);
        Assert.Equal("healthy", health.Health);
        var busy = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.Client.PlanAgentAsync("another request", runtime.Shutdown.Token));
        Assert.Equal("agent_busy", busy.Message);
        // Reproduces the real desktop client's former 700 ms response timeout.
        await Task.Delay(900, runtime.Shutdown.Token);
        runtime.Provider.Release.TrySetResult();
        Assert.True((await request).Available);
        Assert.Equal(1, runtime.Provider.Calls);
        var after = await runtime.Client.GetHealthAsync(runtime.Shutdown.Token);
        Assert.Equal(health.StartedAt, after.StartedAt);
        Assert.Equal("healthy", after.ModelHealth);
    }

    [Fact]
    public async Task ClosingClientCancelsModelAndReleasesAgentSlot()
    {
        await using var runtime = new TestRuntime();
        using var cancel = new CancellationTokenSource();
        var request = runtime.Client.PlanAgentAsync("cancel me", cancel.Token);
        await runtime.Provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await runtime.Provider.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runtime.Provider.Release.TrySetResult();
        RuntimeAgentPlanResponse? next = null;
        for (var attempt = 0; attempt < 50 && next is null; attempt++)
        {
            try { next = await runtime.Client.PlanAgentAsync("try again", runtime.Shutdown.Token); }
            catch (InvalidOperationException exception) when (exception.Message == "agent_busy")
            {
                await Task.Delay(20, runtime.Shutdown.Token);
            }
        }
        Assert.NotNull(next);
        Assert.True(next.Available);
        Assert.Equal(2, runtime.Provider.Calls);
    }

    [Fact]
    public async Task StopAcknowledgesAndCancelsAnActiveModel()
    {
        await using var runtime = new TestRuntime();
        var request = runtime.Client.PlanAgentAsync("waiting", runtime.Shutdown.Token);
        await runtime.Provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await runtime.Client.StopAsync(runtime.Shutdown.Token);
        await runtime.HostTask.WaitAsync(TimeSpan.FromSeconds(5));
        await runtime.Provider.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<Exception>(() => request);
    }

    [Fact]
    public async Task SilentConnectionDoesNotBlockHealthOrShutdown()
    {
        var pipeName = $"Piko.Test.{Guid.NewGuid():N}";
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = new RuntimeIpcServer(request => RuntimeResponse.Ok(
            request.RequestId, "health", RuntimeStatusSnapshot.Starting(DateTimeOffset.UtcNow)), pipeName);
        var running = server.RunAsync(shutdown.Token);
        try
        {
            await using var silent = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await silent.ConnectAsync(shutdown.Token);
            var client = new RuntimeIpcClient(pipeName, TimeSpan.FromSeconds(2));
            Assert.NotNull(await client.GetHealthAsync(shutdown.Token));
        }
        finally
        {
            shutdown.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task OversizedClientFrameDoesNotStopTheServer()
    {
        var pipeName = $"Piko.Test.{Guid.NewGuid():N}";
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = new RuntimeIpcServer(request => RuntimeResponse.Ok(
            request.RequestId, "health", RuntimeStatusSnapshot.Starting(DateTimeOffset.UtcNow)), pipeName);
        var running = server.RunAsync(shutdown.Token);
        try
        {
            await using (var malformed = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await malformed.ConnectAsync(shutdown.Token);
                var bytes = System.Text.Encoding.UTF8.GetBytes(new string('x', 70_000));
                try { await malformed.WriteAsync(bytes, shutdown.Token); }
                catch (IOException) { } // The server can close the rejected frame during this write.
                var buffer = new byte[1];
                try { Assert.Equal(0, await malformed.ReadAsync(buffer, shutdown.Token)); }
                catch (IOException) { }
            }
            var client = new RuntimeIpcClient(pipeName, TimeSpan.FromSeconds(2));
            Assert.NotNull(await client.GetHealthAsync(shutdown.Token));
        }
        finally
        {
            shutdown.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task EscapedUnicodeToolOutputFitsResponseFrame()
    {
        var pipeName = $"Piko.Test.{Guid.NewGuid():N}";
        var output = new string('猫', 32_000);
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = new RuntimeIpcServer(request => RuntimeResponse.Ok(request.RequestId,
            "agent.execution", new RuntimeAgentExecutionResponse(true, "done", output, false)), pipeName);
        var running = server.RunAsync(shutdown.Token);
        try
        {
            var client = new RuntimeIpcClient(pipeName, TimeSpan.FromSeconds(2));
            var result = await client.ExecuteReadProposalAsync("proposal", "workspace", shutdown.Token);
            Assert.Equal(output, result.Output);
        }
        finally
        {
            shutdown.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class TestRuntime : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "PikoInteractionTests", Guid.NewGuid().ToString("N"));
        public readonly CancellationTokenSource Shutdown = new(TimeSpan.FromSeconds(20));
        public readonly BlockingProvider Provider = new();
        public readonly RuntimeIpcClient Client;
        public readonly Task HostTask;

        public TestRuntime()
        {
            var pipeName = $"Piko.Test.{Guid.NewGuid():N}";
            Directory.CreateDirectory(_root);
            RuntimeUserSettingsFile.Save(Path.Combine(_root, "runtime-settings.json"),
                new RuntimeUserSettings { ProviderMode = AiProviderMode.LocalCompatible });
            Client = new RuntimeIpcClient(pipeName, TimeSpan.FromMilliseconds(700));
            HostTask = new PikoRuntimeHost(new RuntimePaths(_root), pipeName: pipeName, aiProvider: Provider)
                .RunAsync(Shutdown.Token);
        }

        public async ValueTask DisposeAsync()
        {
            Shutdown.Cancel();
            try { await HostTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { Shutdown.Dispose(); Directory.Delete(_root, true); }
        }
    }

    private sealed class BlockingProvider : IAiProvider
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;

        public async ValueTask<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            try { await Release.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
            return new AiModelResponse(true,
                "{\"message\":\"Hello\",\"emotion\":\"neutral\",\"action\":\"listen\",\"toolCalls\":[]}", "test", "test");
        }
    }
}
