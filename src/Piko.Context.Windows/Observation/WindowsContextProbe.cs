using System.Diagnostics;
using System.Runtime.InteropServices;
using Piko.Context.Situations;
using Piko.Context.Windows.Native;

namespace Piko.Context.Windows.Observation;

public sealed class WindowsContextProbe : IWindowsContextProbe
{
    private readonly ForegroundApplicationClassifier _classifier;

    public WindowsContextProbe(ForegroundApplicationClassifier? classifier = null)
    {
        _classifier = classifier ?? new ForegroundApplicationClassifier();
    }

    public WindowsContextSnapshot Capture(int idleThresholdSeconds = 120)
    {
        if (idleThresholdSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(idleThresholdSeconds));
        }

        var idleSeconds = CaptureIdleSeconds();
        var isLocked = IsSessionLocked();
        var foreground = NativeMethods.GetForegroundWindow();
        var processName = TryGetProcessName(foreground);
        var (availableMemoryPercent, isOnBattery, batteryPercent) = CaptureSystemHealth();
        return new WindowsContextSnapshot(
            DateTimeOffset.UtcNow,
            isLocked
                ? PresenceState.Locked
                : idleSeconds >= idleThresholdSeconds
                    ? PresenceState.Idle
                    : PresenceState.Active,
            idleSeconds,
            _classifier.Classify(processName),
            IsFullscreen(foreground),
            availableMemoryPercent,
            isOnBattery,
            batteryPercent);
    }

    private static int CaptureIdleSeconds()
    {
        var info = new NativeMethods.LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.LastInputInfo>()
        };
        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return 0;
        }

        var currentLowBits = (uint)(NativeMethods.GetTickCount64() & uint.MaxValue);
        var elapsedMilliseconds = unchecked(currentLowBits - info.Time);
        return (int)Math.Min(int.MaxValue, elapsedMilliseconds / 1000u);
    }

    private static string? TryGetProcessName(nint window)
    {
        if (window == 0)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsSessionLocked()
    {
        var desktop = NativeMethods.OpenInputDesktop(
            0,
            false,
            NativeMethods.DesktopSwitchDesktop);
        if (desktop == 0)
        {
            return false;
        }

        try
        {
            return !NativeMethods.SwitchDesktop(desktop);
        }
        finally
        {
            NativeMethods.CloseDesktop(desktop);
        }
    }

    private static (int AvailableMemoryPercent, bool IsOnBattery, int BatteryPercent) CaptureSystemHealth()
    {
        var memory = new NativeMethods.MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>()
        };
        var availableMemory = NativeMethods.GlobalMemoryStatusEx(ref memory)
            ? (int)Math.Clamp(100 - memory.MemoryLoad, 0, 100)
            : 100;

        if (!NativeMethods.GetSystemPowerStatus(out var power))
        {
            return (availableMemory, false, -1);
        }

        var batteryPercent = power.BatteryLifePercent <= 100
            ? power.BatteryLifePercent
            : -1;
        return (availableMemory, power.AcLineStatus == 0, batteryPercent);
    }

    private static bool IsFullscreen(nint window)
    {
        if (window == 0 || !NativeMethods.GetWindowRect(window, out var windowRect))
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };
        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        const int tolerance = 2;
        return windowRect.Left <= monitorInfo.Monitor.Left + tolerance &&
               windowRect.Top <= monitorInfo.Monitor.Top + tolerance &&
               windowRect.Right >= monitorInfo.Monitor.Right - tolerance &&
               windowRect.Bottom >= monitorInfo.Monitor.Bottom - tolerance;
    }
}
