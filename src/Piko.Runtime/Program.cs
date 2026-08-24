using System.Threading;
using Piko.Runtime.Ipc;

namespace Piko.Runtime;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        RuntimeCommandLine options;
        try
        {
            options = RuntimeCommandLine.Parse(args);
        }
        catch (ArgumentException)
        {
            return 2;
        }

        if (options.HealthCheck)
        {
            try
            {
                var client = new RuntimeIpcClient(options.PipeName, TimeSpan.FromSeconds(3));
                var status = await client.GetHealthAsync().ConfigureAwait(false);
                return status.Health == "healthy" ? 0 : 4;
            }
            catch
            {
                return 4;
            }
        }

        if (options.Stop)
        {
            try
            {
                var client = new RuntimeIpcClient(options.PipeName, TimeSpan.FromSeconds(3));
                await client.StopAsync().ConfigureAwait(false);
                return 0;
            }
            catch
            {
                return 4;
            }
        }

        var isolatedTest = options.SmokeTest || options.StabilityTest;
        var instanceName = isolatedTest
            ? $"Local\\PikoDesktopPet.Runtime.Test.{Environment.ProcessId}"
            : "Local\\PikoDesktopPet.Runtime.SingleInstance";
        using var singleInstance = new Semaphore(1, 1, instanceName);
        var ownsSingleInstance = singleInstance.WaitOne(0);
        if (!ownsSingleInstance)
        {
            return isolatedTest ? 0 : 3;
        }

        using var shutdown = new CancellationTokenSource();
        if (options.SmokeTest)
        {
            shutdown.CancelAfter(TimeSpan.FromSeconds(3));
        }
        else if (options.StabilityTest)
        {
            shutdown.CancelAfter(TimeSpan.FromSeconds(options.StabilityDurationSeconds));
        }

        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        EventHandler processExitHandler = (_, _) => shutdown.Cancel();
        Console.CancelKeyPress += cancelHandler;
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;

        try
        {
            var pipeName = options.PipeName ?? (isolatedTest
                ? $"PikoDesktopPet.Runtime.Test.{Environment.ProcessId}"
                : null);
            var host = new PikoRuntimeHost(new RuntimePaths(options.DataDirectory), pipeName: pipeName);
            await host.RunAsync(shutdown.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch
        {
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
            singleInstance.Release();
        }
    }
}
