using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MediaBrushes = System.Windows.Media.Brushes;
using Piko.Desktop.Services;
using Piko.Context.Interventions;
using Piko.Runtime;
using Piko.Runtime.Ipc;
using Piko.Runtime.Security;
using Piko.Update;
using Piko.World.Behavior;
using Piko.World.Compiler;
using Piko.World.Geometry;
using Piko.World.Model;
using Piko.World.Serialization;
using Piko.World.Windows.Observation;

namespace Piko.Desktop;

public partial class PetWindow : Window
{
    private readonly SettingsStore _settingsStore;
    private readonly AppPaths _paths;
    private readonly AppLogger _logger;
    private readonly FileActivityObserver _fileActivityObserver;
    private readonly DeviceStatePublisher _deviceStatePublisher;
    private readonly RuntimeProcessManager _runtimeProcessManager;
    private readonly UpdateService _updateService;
    private readonly WindowsSnapshotProvider _snapshotProvider = new();
    private readonly DesktopWorldCompiler _worldCompiler = new();
    private readonly PetController _controller = new();
    private readonly PetMind _mind = new();
    private readonly DispatcherTimer _worldTimer;
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _runtimeSupervisorTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ContextMenuStrip _trayMenu;
    private readonly System.Drawing.Icon _trayIconImage;
    private Forms.ToolStripMenuItem? _modelStatusMenuItem;
    private readonly bool _recoveredFromCrash;
    private readonly bool _smokeTest;
    private readonly TimeSpan? _automaticShutdownAfter;

    private PikoSettings _settings;
    private DesktopWorld? _world;
    private nint _handle;
    private TimeSpan _previousTick;
    private PixelPoint _lastCursor;
    private DateTimeOffset _lastCursorMovement = DateTimeOffset.UtcNow;
    private PetCommand? _pendingCommand;
    private bool _mousePressed;
    private bool _dragging;
    private System.Drawing.Point _mouseDownPoint;
    private bool _preparedForExit;
    private long _visualFrame;
    private bool _runtimeCheckInProgress;
    private bool _userHidden;
    private bool _suppressedForFullscreen;
    private bool _runtimeUnavailableLogged;
    private DateTimeOffset? _runtimeStartedAt;
    private long _lastInterventionSequence;
    private PetReaction? _pendingReaction;
    private AgentWindow? _agentWindow;
    private int _clickSequence;

    public PetWindow(
        PikoSettings settings,
        bool recoveredFromCrash,
        bool smokeTest,
        TimeSpan? automaticShutdownAfter,
        AppPaths paths,
        SettingsStore settingsStore,
        AppLogger logger,
        FileActivityObserver fileActivityObserver,
        DeviceStatePublisher deviceStatePublisher,
        RuntimeProcessManager runtimeProcessManager)
    {
        InitializeComponent();
        _settings = settings;
        _recoveredFromCrash = recoveredFromCrash;
        _smokeTest = smokeTest;
        _automaticShutdownAfter = automaticShutdownAfter;
        _paths = paths;
        _settingsStore = settingsStore;
        _logger = logger;
        _fileActivityObserver = fileActivityObserver;
        _deviceStatePublisher = deviceStatePublisher;
        _runtimeProcessManager = runtimeProcessManager;
        _updateService = new UpdateService(paths.Root, logger);

        _trayMenu = BuildTrayMenu();
        _trayIconImage = PikoTrayIconFactory.Create();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayIconImage,
            Text = "Piko Desktop Pet",
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => QueueCommand(PetCommand.Recall);

        try
        {
            if (!_smokeTest)
            {
                StartupRegistration.Apply(_settings.LaunchAtStartup);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Could not synchronize startup registration", exception);
        }

        _worldTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _worldTimer.Tick += (_, _) =>
        {
            UpdateFullscreenSuppression();
            CaptureWorld();
        };

        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _animationTimer.Tick += (_, _) => TickPet();

        _runtimeSupervisorTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _runtimeSupervisorTimer.Tick += async (_, _) => await CheckRuntimeAsync();

        SourceInitialized += Window_SourceInitialized;
        Loaded += Window_Loaded;
    }

    public void PrepareExit()
    {
        if (_preparedForExit)
        {
            return;
        }

        _preparedForExit = true;
        _worldTimer.Stop();
        _animationTimer.Stop();
        _runtimeSupervisorTimer.Stop();
        SaveSettings(cleanExit: true);

        if (_handle != 0)
        {
            NativeWindowServices.UnregisterHotKey(_handle, NativeWindowServices.HotkeyId);
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIconImage.Dispose();
        _trayMenu.Dispose();
        _logger.Info("Piko exited cleanly");
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _handle = NativeWindowServices.HandleOf(this);
        NativeWindowServices.ConfigurePetWindow(_handle, _settings.ClickThrough);

        if (HwndSource.FromHwnd(_handle) is { } source)
        {
            source.AddHook(WindowMessageHook);
        }

        if (!NativeWindowServices.RegisterHotKey(
                _handle,
                NativeWindowServices.HotkeyId,
                NativeWindowServices.ModControl | NativeWindowServices.ModAlt,
                NativeWindowServices.VkP))
        {
            _logger.Info("Ctrl+Alt+P hotkey was already in use");
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateFullscreenSuppression();
        CaptureWorld();
        _previousTick = _clock.Elapsed;
        _worldTimer.Start();
        _animationTimer.Start();

        if (!_smokeTest)
        {
            _runtimeSupervisorTimer.Start();
            _ = CheckRuntimeAsync();
        }

        if (_automaticShutdownAfter is { } shutdownAfter)
        {
            var shutdownTimer = new DispatcherTimer
            {
                Interval = shutdownAfter
            };
            shutdownTimer.Tick += (_, _) =>
            {
                shutdownTimer.Stop();
                System.Windows.Application.Current.Shutdown();
            };
            shutdownTimer.Start();
        }
    }

    private nint WindowMessageHook(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == NativeWindowServices.WmHotkey && wParam.ToInt32() == NativeWindowServices.HotkeyId)
        {
            QueueCommand(PetCommand.Recall);
            handled = true;
        }

        return 0;
    }

    private void CaptureWorld()
    {
        try
        {
            var snapshot = _snapshotProvider.Capture(includeInvisibleWindows: false);
            _world = _worldCompiler.Compile(snapshot);

            if (_settings.FileActivityAwarenessEnabled)
            {
                _fileActivityObserver.UpdateDesktop(snapshot);
            }

            if (_controller.State.Feet == default)
            {
                PixelPoint? preferred = null;
                if (!_recoveredFromCrash &&
                    _settings.SavedFeetX is { } x && _settings.SavedFeetY is { } y &&
                    IsNearDesktop(snapshot.VirtualDesktop, x, y))
                {
                    preferred = new PixelPoint(x, y);
                }

                _controller.Initialize(_world, preferred);
                if (_recoveredFromCrash)
                {
                    _pendingCommand = PetCommand.Recall;
                }
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Desktop capture failed", exception);
        }
    }

    private void TickPet()
    {
        if (_world is null || _handle == 0)
        {
            return;
        }

        var now = _clock.Elapsed;
        var elapsed = Math.Max(0.001, (now - _previousTick).TotalSeconds);
        _previousTick = now;

        var cursor = Forms.Cursor.Position;
        var cursorPoint = new PixelPoint(cursor.X, cursor.Y);
        if (Distance(cursorPoint, _lastCursor) > 3)
        {
            _lastCursorMovement = DateTimeOffset.UtcNow;
            _lastCursor = cursorPoint;
        }

        var dragFeet = _dragging
            ? new PixelPoint(cursor.X, cursor.Y + 58)
            : (PixelPoint?)null;
        var fileActivity = _settings.FileActivityAwarenessEnabled
            ? _fileActivityObserver.Current
            : FileActivitySignal.None;

        var input = new PetRuntimeInput(
            cursorPoint,
            DateTimeOffset.UtcNow - _lastCursorMovement >= TimeSpan.FromSeconds(2),
            _dragging,
            dragFeet,
            fileActivity,
            _pendingCommand,
            _settings.AutonomousBehaviorEnabled,
            _settings.PointerAwarenessEnabled,
            _settings.WindowExplorationEnabled,
            _pendingReaction,
            _mind.Advance(elapsed));
        _pendingCommand = null;
        _pendingReaction = null;

        var state = _controller.Tick(_world, input, elapsed);
        NativeWindowServices.Position(_handle, state.Feet.X, state.Feet.Y);
        RenderState(state, fileActivity);

        try
        {
            _deviceStatePublisher.Publish(state);
        }
        catch (Exception exception)
        {
            _logger.Error("Could not publish ESP32 projection state", exception);
        }
    }

    private void RenderState(PetBodyState state, FileActivitySignal activity)
    {
        _visualFrame++;
        var phase = _visualFrame * 0.12;
        var emotion = state.Emotion == default ? PetEmotionState.Baseline : state.Emotion;
        FacingTransform.ScaleX = state.FacingRight ? 1 : -1;
        BodyMotion.Y = state.Mode switch
        {
            PetMode.Walking => Math.Abs(Math.Sin(phase)) * -4,
            PetMode.Jumping or PetMode.Falling => -3,
            PetMode.Greeting => Math.Sin(phase * 1.8) * 4,
            PetMode.Celebrating => Math.Abs(Math.Sin(phase * 2.2)) * -9,
            PetMode.Concerned => 3 + Math.Sin(phase * 0.35),
            _ => Math.Sin(phase * 0.35) * 1.5
        };
        ModeRotation.Angle = state.Mode switch
        {
            PetMode.Climbing => state.FacingRight ? -12 : 12,
            PetMode.Jumping => state.FacingRight ? 8 : -8,
            PetMode.Falling => state.FacingRight ? -6 : 6,
            PetMode.Concerned => state.FacingRight ? 5 : -5,
            PetMode.Celebrating => Math.Sin(phase * 1.8) * 7,
            _ => 0
        };

        var blinkPeriod = Math.Max(80, (int)Math.Round(165 - emotion.Arousal * 60));
        var blink = state.Mode == PetMode.Resting || _visualFrame % blinkPeriod is >= 0 and <= 5;
        LeftEye.Height = blink || state.Mode == PetMode.Celebrating
            ? 2
            : state.Mode == PetMode.Falling
                ? 18
                : state.Mode == PetMode.Concerned
                    ? 9
                    : 14;
        RightEye.Height = LeftEye.Height;
        var eyeTop = blink || state.Mode == PetMode.Celebrating
            ? 57
            : state.Mode == PetMode.Concerned
                ? 53
                : 49;
        Canvas.SetTop(LeftEye, eyeTop);
        Canvas.SetTop(RightEye, eyeTop);
        BodyLayer.Opacity = 0.82 + emotion.Energy * 0.18;
        SpeechBubble.Background = state.Mode switch
        {
            PetMode.Concerned => MediaBrushes.LightGoldenrodYellow,
            PetMode.Celebrating => MediaBrushes.Honeydew,
            _ => MediaBrushes.White
        };

        var peeking = state.Mode == PetMode.Peeking;
        BodyLayer.Visibility = peeking ? Visibility.Collapsed : Visibility.Visible;
        PeekLayer.Visibility = peeking ? Visibility.Visible : Visibility.Collapsed;
        SpeechBubble.Visibility = !peeking && _settings.ShowMessages && state.SpeechVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        SpeechText.Text = state.Message;

        var observing = state.Mode == PetMode.ObservingTransfer && activity.IsActive;
        TransferBadge.Visibility = observing ? Visibility.Visible : Visibility.Collapsed;
        TransferGlyph.Visibility = observing ? Visibility.Visible : Visibility.Collapsed;
        ToolTip = $"Piko · {StateLabel(state.Mode)}\n{state.Message}";
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.ClickThrough)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            _clickSequence++;
            OpenSettings();
            e.Handled = true;
            return;
        }

        _mousePressed = true;
        _dragging = false;
        _mouseDownPoint = Forms.Cursor.Position;
        CaptureMouse();
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_mousePressed || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var cursor = Forms.Cursor.Position;
        if (Math.Abs(cursor.X - _mouseDownPoint.X) + Math.Abs(cursor.Y - _mouseDownPoint.Y) >= 6)
        {
            _dragging = true;
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mousePressed)
        {
            return;
        }

        var wasDragging = _dragging;
        _mousePressed = false;
        _dragging = false;
        ReleaseMouseCapture();

        if (!wasDragging)
        {
            var clickSequence = ++_clickSequence;
            _ = OpenAgentAfterSingleClickAsync(clickSequence);
        }

        e.Handled = true;
    }

    private async Task OpenAgentAfterSingleClickAsync(int clickSequence)
    {
        await Task.Delay(Forms.SystemInformation.DoubleClickTime);
        if (clickSequence == _clickSequence)
        {
            OpenAgent();
        }
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_settings.ClickThrough)
        {
            _trayMenu.Show(Forms.Cursor.Position);
            e.Handled = true;
        }
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        _modelStatusMenuItem = new Forms.ToolStripMenuItem("模型：等待后台状态")
        {
            Enabled = false
        };
        menu.Items.Add(_modelStatusMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("召回 Piko  (Ctrl+Alt+P)", null, (_, _) => QueueCommand(PetCommand.Recall));
        menu.Items.Add("显示 / 隐藏", null, (_, _) => ToggleVisibility());

        var demo = new Forms.ToolStripMenuItem("演示互动");
        demo.DropDownItems.Add("沿窗口散步", null, (_, _) => QueueCommand(PetCommand.Walk));
        demo.DropDownItems.Add("爬窗口边缘", null, (_, _) => QueueCommand(PetCommand.Climb));
        demo.DropDownItems.Add("跳到另一个窗口", null, (_, _) => QueueCommand(PetCommand.Jump));
        demo.DropDownItems.Add("躲到屏幕外探头", null, (_, _) => QueueCommand(PetCommand.Peek));
        demo.DropDownItems.Add("把窗口当小床", null, (_, _) => QueueCommand(PetCommand.Rest));
        menu.Items.Add(demo);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("问 Piko…", null, (_, _) => Dispatcher.Invoke(OpenAgent));
        menu.Items.Add("查看 / 删除本地记忆…", null, (_, _) => Dispatcher.Invoke(OpenMemory));
        menu.Items.Add("查看后台状态", null, (_, _) => Dispatcher.Invoke(ShowRuntimeStatus));
        menu.Items.Add("检查正式版更新…", null, (_, _) => Dispatcher.Invoke(() => _ = CheckForUpdatesAsync()));
        menu.Items.Add("设置…", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("导出隐私诊断快照", null, (_, _) => Dispatcher.Invoke(ExportDiagnosticSnapshot));
        menu.Items.Add("打开本地状态目录", null, (_, _) => OpenStateFolder());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 Piko", null, (_, _) => Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown()));
        return menu;
    }

    private void OpenAgent()
    {
        if (_agentWindow is { IsLoaded: true })
        {
            _agentWindow.Activate();
            return;
        }

        _pendingReaction = _mind.React(PetStimulus.RespondToUser, _settings.ShowMessages);
        var window = new AgentWindow(_runtimeProcessManager, _logger, result =>
        {
            Dispatcher.Invoke(() =>
            {
                _pendingReaction = _mind.ReactToModel(
                    result.Message,
                    result.Emotion,
                    result.Action,
                    _settings.ShowMessages);
                _logger.Info($"Model expression accepted (emotion={result.Emotion}, action={result.Action})");
            });
        })
        {
            Owner = this
        };
        _agentWindow = window;
        window.Closed += (_, _) => _agentWindow = null;
        window.Show();
    }

    private void OpenMemory()
    {
        var window = new MemoryWindow(_runtimeProcessManager, _logger)
        {
            Owner = this
        };
        window.Show();
    }

    private async void ShowRuntimeStatus()
    {
        var status = await _runtimeProcessManager.TryGetStatusAsync();
        if (status is null)
        {
            System.Windows.MessageBox.Show(
                "Piko 后台当前未连接。桌宠仍可运行，但行为感知与 AI Agent 暂不可用。",
                "Piko 后台",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        System.Windows.MessageBox.Show(
            $"状态：{status.Health}\n版本：{status.Version}\n模型：{DescribeModelStatus(status)}\n情境：{status.Situation}\n心跳：{status.LastHeartbeatAt.ToLocalTime():HH:mm:ss}",
            "Piko 后台",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var result = await _updateService.CheckAsync();
            if (!result.IsUpdateAvailable)
            {
                System.Windows.MessageBox.Show(
                    $"当前版本 {PikoProductInfo.Version} 已是最新正式版。",
                    "Piko 更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!_updateService.CanInstallAutomatically(result.Manifest))
            {
                var open = System.Windows.MessageBox.Show(
                    $"发现版本 {result.Manifest.Version}。当前构建尚未配置可信发布者证书，因此不会自动执行下载文件。是否打开 GitHub Release 页面？",
                    "Piko 更新",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (open == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(result.Manifest.ReleasePage.ToString()) { UseShellExecute = true })?.Dispose();
                }

                return;
            }

            var install = System.Windows.MessageBox.Show(
                $"发现已签名版本 {result.Manifest.Version}。下载、验证并安装吗？Piko 会自动重启。",
                "Piko 更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (install != MessageBoxResult.Yes)
            {
                return;
            }

            if (await _updateService.DownloadVerifyAndStartAsync(result.Manifest))
            {
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                System.Windows.MessageBox.Show(
                    "更新包未通过签名或完整性校验，未执行安装。",
                    "Piko 更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Could not check for updates", exception);
            System.Windows.MessageBox.Show(
                "暂时无法获取正式版更新清单。当前版本不会受到影响。",
                "Piko 更新",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private async Task CheckRuntimeAsync()
    {
        if (_runtimeCheckInProgress)
        {
            return;
        }

        _runtimeCheckInProgress = true;
        try
        {
            var status = await _runtimeProcessManager.EnsureStartedAsync();
            if (status is null)
            {
                if (!_runtimeUnavailableLogged)
                {
                    _logger.Info("Piko continues in desktop-only mode because Runtime is unavailable");
                    _runtimeUnavailableLogged = true;
                }
            }
            else
            {
                _runtimeUnavailableLogged = false;
                ProcessRuntimeStatus(status);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Piko Runtime connection failed", exception);
        }
        finally
        {
            _runtimeCheckInProgress = false;
        }
    }

    private async void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings);
        if (dialog.ShowDialog() == true && dialog.Result is { } result)
        {
            _settings = result with { LastExitWasClean = false };
            _settingsStore.Save(_settings);
            try
            {
                var credentials = new WindowsCredentialStore();
                if (dialog.ClearApiKey)
                {
                    credentials.Delete(RuntimeSecretNames.OpenAiApiKey);
                }
                else if (dialog.ApiKeyUpdate is { } apiKey)
                {
                    credentials.Save(RuntimeSecretNames.OpenAiApiKey, apiKey);
                }
            }
            catch (Exception exception)
            {
                _logger.Error("Could not update AI credential", exception);
                System.Windows.MessageBox.Show(
                    "API Key 无法保存到 Windows 凭据管理器。其他设置已保存，AI 将保持不可用。",
                    "Piko AI 设置",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            RuntimeUserSettingsFile.Save(
                _paths.RuntimeSettingsFile,
                _settings.ToRuntimeUserSettings());
            await RestartRuntimeAfterSettingsChangeAsync();
            if (dialog.TestConnectionRequested)
            {
                await TestModelConnectionAsync();
            }
            try
            {
                StartupRegistration.Apply(_settings.LaunchAtStartup);
            }
            catch (Exception exception)
            {
                _logger.Error("Could not update startup registration", exception);
                System.Windows.MessageBox.Show(
                    "开机启动设置失败，但其他设置已经保存。",
                    "Piko",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            if (_handle != 0)
            {
                NativeWindowServices.ConfigurePetWindow(_handle, _settings.ClickThrough);
            }
        }
    }

    private async Task RestartRuntimeAfterSettingsChangeAsync()
    {
        try
        {
            await _runtimeProcessManager.RestartAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("Could not restart Runtime after settings changed", exception);
        }
    }

    private void OpenStateFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", _paths.Root) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _logger.Error("Could not open local state folder", exception);
        }
    }

    private void ExportDiagnosticSnapshot()
    {
        if (_world is null)
        {
            return;
        }

        try
        {
            var directory = Path.Combine(_paths.Root, "diagnostics");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"piko-world-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(path, DesktopSnapshotJson.Serialize(_world.Source));
            _trayIcon.ShowBalloonTip(
                2500,
                "Piko 诊断快照已导出",
                "快照不包含窗口标题或文件内容。",
                Forms.ToolTipIcon.Info);
            _logger.Info("Privacy-conscious desktop snapshot exported");
        }
        catch (Exception exception)
        {
            _logger.Error("Could not export diagnostic snapshot", exception);
        }
    }

    private void ToggleVisibility()
    {
        Dispatcher.Invoke(() =>
        {
            if (_suppressedForFullscreen)
            {
                _userHidden = false;
                return;
            }

            _userHidden = IsVisible;
            ApplyVisibilityState();
            if (!_userHidden)
            {
                _pendingCommand = PetCommand.Recall;
            }
        });
    }

    private async Task TestModelConnectionAsync()
    {
        if (_settings.ProviderMode == AiProviderMode.Disabled)
        {
            System.Windows.MessageBox.Show(
                "模型接入仍处于关闭状态。请先选择 OpenAI API 或本地兼容模型。",
                "Piko 模型连接测试",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        RuntimeAgentPlanResponse result;
        try
        {
            result = await _runtimeProcessManager.PlanAgentAsync(
                "This is a connection test. Reply with a very short greeting, neutral emotion, listen action, and no tools.");
        }
        catch (Exception exception)
        {
            _logger.Error("Model connection test failed", exception);
            System.Windows.MessageBox.Show(
                "无法连接 Piko Runtime。请稍后重试或查看后台状态。",
                "Piko 模型连接测试",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var latestStatus = await _runtimeProcessManager.TryGetStatusAsync();
        if (latestStatus is not null)
        {
            UpdateModelStatus(latestStatus);
        }
        System.Windows.MessageBox.Show(
            result.Available
                ? $"连接成功。\n提供方：{result.Provider}\n模型：{result.Model}\nPiko：{result.Message}"
                : DescribeModelError(result.Reason),
            "Piko 模型连接测试",
            MessageBoxButton.OK,
            result.Available ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void QueueCommand(PetCommand command)
    {
        Dispatcher.Invoke(() =>
        {
            _userHidden = false;
            ApplyVisibilityState();
            _pendingCommand = command;
        });
    }

    private void UpdateFullscreenSuppression()
    {
        if (_smokeTest || _handle == 0)
        {
            return;
        }

        var shouldSuppress = NativeWindowServices.IsForegroundWindowFullscreen(_handle);
        if (shouldSuppress == _suppressedForFullscreen)
        {
            return;
        }

        _suppressedForFullscreen = shouldSuppress;
        ApplyVisibilityState();
        _logger.Info(shouldSuppress
            ? "Piko hidden while a fullscreen application is active"
            : "Piko restored after fullscreen application ended");
    }

    private void ApplyVisibilityState()
    {
        var shouldShow = !_userHidden && !_suppressedForFullscreen;
        if (shouldShow && !IsVisible)
        {
            Show();
        }
        else if (!shouldShow && IsVisible)
        {
            Hide();
        }
    }

    private void ProcessRuntimeStatus(RuntimeStatusSnapshot status)
    {
        if (_runtimeStartedAt != status.StartedAt)
        {
            _runtimeStartedAt = status.StartedAt;
            _lastInterventionSequence = 0;
        }

        UpdateModelStatus(status);

        if (status.InterventionSequence <= _lastInterventionSequence)
        {
            return;
        }

        _lastInterventionSequence = status.InterventionSequence;
        var stimulus = status.LastIntervention switch
        {
            InterventionKind.SilentConcern => PetStimulus.SilentConcern,
            InterventionKind.Greet => PetStimulus.Greet,
            InterventionKind.OfferHelp => PetStimulus.OfferHelp,
            InterventionKind.Celebrate => PetStimulus.Celebrate,
            InterventionKind.RespondToUser => PetStimulus.RespondToUser,
            _ => (PetStimulus?)null
        };
        if (stimulus is null)
        {
            return;
        }

        _pendingReaction = _mind.React(stimulus.Value, status.InterventionShouldSpeak);
        _logger.Info(
            $"PetMind accepted {status.InterventionSemanticAction} " +
            $"(emotion={_mind.Emotion.Valence:F2}/{_mind.Emotion.Arousal:F2}, " +
            $"speak={_pendingReaction.ShouldSpeak})");
        if (status.InterventionShouldSpeak && status.LastIntervention != InterventionKind.RespondToUser)
        {
            _ = EnrichProactiveReactionAsync(status);
        }
    }

    private void UpdateModelStatus(RuntimeStatusSnapshot status)
    {
        var description = DescribeModelStatus(status);
        if (_modelStatusMenuItem is not null)
        {
            _modelStatusMenuItem.Text = $"模型：{description}";
        }
        var tooltip = $"Piko · {description}";
        _trayIcon.Text = tooltip[..Math.Min(63, tooltip.Length)];
    }

    private static string DescribeModelStatus(RuntimeStatusSnapshot status)
    {
        var provider = status.ProviderMode switch
        {
            AiProviderMode.OpenAiApi => "OpenAI API",
            AiProviderMode.LocalCompatible => "本地模型",
            _ => "已关闭"
        };
        return status.ModelHealth switch
        {
            "healthy" => $"{provider} 已连接",
            "error" => $"{provider} 异常（{DescribeModelError(status.ModelLastError)}）",
            "not_tested" => $"{provider} 未测试",
            _ => provider
        };
    }

    private static string DescribeModelError(string reason) => reason switch
    {
        "api_key_unavailable" => "没有找到 API Key。请在设置中填写后保存。",
        "credential_unavailable" => "Windows 凭据管理器当前不可用。",
        "http_400" or "invalid_plan_shape" or "invalid_plan_json" => "请求或模型输出格式不兼容。",
        "http_401" => "API Key 无效或已失效。",
        "http_403" => "当前账号或项目没有调用该模型的权限。",
        "http_404" => "API 地址或模型 ID 不存在。",
        "http_422" => "模型不接受当前结构化请求。",
        "http_429" => "请求达到速率或额度限制，请稍后重试并检查账户额度。",
        "timeout" => "连接超时。本地模型可能尚未加载完成。",
        "provider_error" => "无法连接服务，或服务返回了无效响应。",
        "model_disabled" => "模型尚未启用。",
        _ when reason.StartsWith("http_5", StringComparison.Ordinal) => "模型服务暂时不可用。",
        _ => $"连接失败：{reason}"
    };

    private async Task EnrichProactiveReactionAsync(RuntimeStatusSnapshot status)
    {
        try
        {
            var result = await _runtimeProcessManager.PlanAgentAsync(
                "Generate Piko's brief proactive expression for this already-approved local event. " +
                $"Semantic event: {status.InterventionSemanticAction}; reason: {status.InterventionReason}. " +
                "Do not propose tools. Return a warm, concise pet response and an appropriate emotion/action.");
            if (!result.Available || result.ToolProposals.Count > 0)
            {
                return;
            }

            _pendingReaction = _mind.ReactToModel(
                result.Message,
                result.Emotion,
                result.Action,
                _settings.ShowMessages);
            _logger.Info($"Proactive model expression accepted (emotion={result.Emotion}, action={result.Action})");
        }
        catch (Exception exception)
        {
            _logger.Info($"Proactive model expression unavailable; local PetMind fallback kept ({exception.GetType().Name})");
        }
    }

    private void SaveSettings(bool cleanExit)
    {
        var feet = _controller.State.Feet;
        _settings = _settings with
        {
            LastExitWasClean = cleanExit,
            SavedFeetX = feet == default ? _settings.SavedFeetX : feet.X,
            SavedFeetY = feet == default ? _settings.SavedFeetY : feet.Y
        };
        _settingsStore.Save(_settings);
    }

    private static bool IsNearDesktop(PixelRect desktop, double x, double y) =>
        x >= desktop.Left - 100 && x <= desktop.Right + 100 &&
        y >= desktop.Top - 100 && y <= desktop.Bottom + 100;

    private static double Distance(PixelPoint first, PixelPoint second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static string StateLabel(PetMode mode) => mode switch
    {
        PetMode.Standing => "站立",
        PetMode.Walking => "散步",
        PetMode.Falling => "下落",
        PetMode.Dragging => "被抱起",
        PetMode.Climbing => "攀爬",
        PetMode.Jumping => "跳跃",
        PetMode.Peeking => "探头",
        PetMode.PointerDwell => "鼠标旁驻足",
        PetMode.ObservingTransfer => "观察文件活动",
        PetMode.Resting => "休息",
        PetMode.Greeting => "打招呼",
        PetMode.Concerned => "安静关心",
        PetMode.Celebrating => "庆祝",
        _ => mode.ToString()
    };
}

