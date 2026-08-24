using System.Diagnostics;
using System.Runtime.InteropServices;
using Piko.World.Geometry;
using Piko.World.Model;
using Piko.World.Windows.Native;

namespace Piko.World.Windows.Observation;

public sealed class WindowsSnapshotProvider
{
    private const uint MonitorInfoPrimary = 1;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    public DesktopSnapshot Capture(bool includeInvisibleWindows = true)
    {
        var monitorsByHandle = CaptureMonitors();
        var windows = CaptureWindows(monitorsByHandle, includeInvisibleWindows);
        NativeMethods.GetCursorPos(out var cursor);

        return DesktopSnapshot.Create(
            monitorsByHandle.Values,
            windows,
            new PixelPoint(cursor.X, cursor.Y));
    }

    private static Dictionary<nint, MonitorSnapshot> CaptureMonitors()
    {
        var monitors = new Dictionary<nint, MonitorSnapshot>();

        NativeMethods.EnumDisplayMonitors(
            0,
            0,
            (nint handle, nint _, ref NativeMethods.Rect _, nint _) =>
            {
                var info = new NativeMethods.MonitorInfo
                {
                    Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
                };

                if (!NativeMethods.GetMonitorInfo(handle, ref info))
                {
                    return true;
                }

                var id = $"{handle.ToInt64():X}";
                var (dpiX, dpiY) = TryGetMonitorDpi(handle);
                monitors[handle] = new MonitorSnapshot(
                    id,
                    ToPixelRect(info.Monitor),
                    ToPixelRect(info.WorkArea),
                    dpiX,
                    dpiY,
                    (info.Flags & MonitorInfoPrimary) != 0);

                return true;
            },
            0);

        return monitors;
    }

    private IReadOnlyList<WindowSnapshot> CaptureWindows(
        IReadOnlyDictionary<nint, MonitorSnapshot> monitors,
        bool includeInvisibleWindows)
    {
        var windows = new List<WindowSnapshot>();
        var zOrder = 0;

        NativeMethods.EnumWindows(
            (window, _) =>
            {
                var snapshot = CaptureWindow(window, zOrder++, monitors);
                if (snapshot is not null && (includeInvisibleWindows || snapshot.IsVisible))
                {
                    windows.Add(snapshot);
                }

                return true;
            },
            0);

        return windows;
    }

    private WindowSnapshot? CaptureWindow(
        nint window,
        int zOrder,
        IReadOnlyDictionary<nint, MonitorSnapshot> monitors)
    {
        var visible = NativeMethods.IsWindowVisible(window);
        var minimized = NativeMethods.IsIconic(window);
        var maximized = NativeMethods.IsZoomed(window);

        if (!TryGetBounds(window, out var bounds))
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        var className = GetClassName(window);
        var cloaked = TryGetCloaked(window);
        var exStyle = NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle).ToInt64();
        var isToolWindow = (exStyle & NativeMethods.WsExToolWindow) != 0;
        var monitorHandle = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
        monitors.TryGetValue(monitorHandle, out var monitor);
        var dpi = TryGetDpi(window);

        var exclusionReason = GetExclusionReason(
            visible,
            minimized,
            cloaked,
            isToolWindow,
            processId,
            className,
            bounds);

        return new WindowSnapshot(
            $"{window.ToInt64():X}",
            bounds,
            zOrder,
            visible,
            minimized,
            maximized,
            cloaked,
            exclusionReason is null,
            exclusionReason,
            monitor?.Id ?? "unknown",
            dpi,
            dpi,
            className);
    }

    private string? GetExclusionReason(
        bool visible,
        bool minimized,
        bool cloaked,
        bool toolWindow,
        uint processId,
        string className,
        PixelRect bounds)
    {
        if (!visible) return "not_visible";
        if (minimized) return "minimized";
        if (cloaked) return "cloaked";
        if (toolWindow) return "tool_window";
        if (processId == _ownProcessId) return "own_process";
        if (bounds.IsEmpty) return "empty_bounds";
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd") return "shell_window";
        return null;
    }

    private static bool TryGetBounds(nint window, out PixelRect bounds)
    {
        var result = NativeMethods.DwmGetWindowAttribute(
            window,
            NativeMethods.DwmwaExtendedFrameBounds,
            out NativeMethods.Rect frame,
            Marshal.SizeOf<NativeMethods.Rect>());

        if (result != 0 && !NativeMethods.GetWindowRect(window, out frame))
        {
            bounds = default;
            return false;
        }

        bounds = ToPixelRect(frame);
        return true;
    }

    private static bool TryGetCloaked(nint window)
    {
        var result = NativeMethods.DwmGetWindowAttribute(
            window,
            NativeMethods.DwmwaCloaked,
            out int cloaked,
            sizeof(int));
        return result == 0 && cloaked != 0;
    }

    private static uint TryGetDpi(nint window)
    {
        try
        {
            return NativeMethods.GetDpiForWindow(window) is var dpi && dpi > 0 ? dpi : 96;
        }
        catch (EntryPointNotFoundException)
        {
            return 96;
        }
    }

    private static (uint X, uint Y) TryGetMonitorDpi(nint monitor)
    {
        try
        {
            return NativeMethods.GetDpiForMonitor(
                       monitor,
                       NativeMethods.MdtEffectiveDpi,
                       out var dpiX,
                       out var dpiY) == 0 && dpiX > 0 && dpiY > 0
                ? (dpiX, dpiY)
                : (96, 96);
        }
        catch (EntryPointNotFoundException)
        {
            return (96, 96);
        }
        catch (DllNotFoundException)
        {
            return (96, 96);
        }
    }

    private static string GetClassName(nint window)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetClassName(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static PixelRect ToPixelRect(NativeMethods.Rect rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
