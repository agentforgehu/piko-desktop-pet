using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Piko.Desktop.Services;

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

        _singleInstance = new Mutex(true, "Local\\PikoDesktopPet.SingleInstance", out var createdNew);
        _ownsSingleInstance = createdNew;
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("Piko 已经在运行。按 Ctrl+Alt+P 可以把它召回。", "Piko");
            Shutdown();
            return;
        }

        var paths = new AppPaths();
        var logger = new AppLogger(paths);
        var store = new SettingsStore(paths);
        var loaded = store.Load();
        var recoveredFromCrash = !loaded.LastExitWasClean;
        var settings = loaded with { LastExitWasClean = false };
        store.Save(settings);

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
            e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase),
            paths,
            store,
            logger,
            _fileActivityObserver,
            new DeviceStatePublisher(paths));
        MainWindow = _petWindow;
        _petWindow.Show();
        logger.Info(recoveredFromCrash
            ? "Piko started with crash recovery recall"
            : "Piko started");
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
