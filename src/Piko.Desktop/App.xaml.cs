using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Piko.Desktop.Services;
using Piko.Runtime;

namespace Piko.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private bool _ownsSingleInstance;
    private PetWindow? _petWindow;
    private FileActivityObserver? _fileActivityObserver;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var smokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        var stabilityTest = e.Args.Contains("--stability-test", StringComparer.OrdinalIgnoreCase);
        var isolatedTest = smokeTest || stabilityTest;
        var automaticShutdownAfter = smokeTest
            ? TimeSpan.FromSeconds(3)
            : stabilityTest
                ? TimeSpan.FromSeconds(ReadBoundedIntegerArgument(e.Args, "--duration-seconds", 1800, 10, 86_400))
                : (TimeSpan?)null;
        var dataDirectory = ReadArgumentValue(e.Args, "--data-dir");

        var instanceName = isolatedTest
            ? $"Local\\PikoDesktopPet.Test.{Environment.ProcessId}"
            : "Local\\PikoDesktopPet.SingleInstance";
        _singleInstance = new Mutex(true, instanceName, out var createdNew);
        _ownsSingleInstance = createdNew;
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("Piko 已经在运行。按 Ctrl+Alt+P 可以把它召回。", "Piko");
            Shutdown();
            return;
        }

        var paths = new AppPaths(dataDirectory);
        var logger = new AppLogger(paths);
        var store = new SettingsStore(paths);
        var loaded = store.Load();
        var recoveredFromCrash = !loaded.LastExitWasClean;
        var settings = loaded with { LastExitWasClean = false };
        store.Save(settings);
        RuntimeUserSettingsFile.Save(paths.RuntimeSettingsFile, settings.ToRuntimeUserSettings());

        DispatcherUnhandledException += (_, args) =>
        {
            logger.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.Error("Unhandled process exception", args.ExceptionObject as Exception);

        _fileActivityObserver = new FileActivityObserver(logger);
        _petWindow = new PetWindow(
            settings,
            recoveredFromCrash,
            isolatedTest,
            automaticShutdownAfter,
            paths,
            store,
            logger,
            _fileActivityObserver,
            new DeviceStatePublisher(paths),
            new RuntimeProcessManager(logger));
        MainWindow = _petWindow;
        _petWindow.Show();
        logger.Info(recoveredFromCrash
            ? "Piko started with crash recovery recall"
            : "Piko started");
    }

    private static string? ReadArgumentValue(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(args[index + 1]) ? null : args[index + 1];
            }
        }

        return null;
    }

    private static int ReadBoundedIntegerArgument(
        string[] args,
        string name,
        int defaultValue,
        int minimum,
        int maximum)
    {
        var raw = ReadArgumentValue(args, name);
        if (raw is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, out var value) || value < minimum || value > maximum)
        {
            throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
        }

        return value;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _petWindow?.PrepareExit();
        _fileActivityObserver?.Dispose();
        if (_ownsSingleInstance)
        {
            _singleInstance?.ReleaseMutex();
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
