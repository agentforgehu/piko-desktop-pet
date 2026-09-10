using System.Windows;

namespace Piko.Desktop.Services;

internal static class WindowSizing
{
    internal static void FitToWorkArea(Window window)
    {
        var area = SystemParameters.WorkArea;
        window.MinWidth = Math.Min(window.MinWidth, area.Width);
        window.MinHeight = Math.Min(window.MinHeight, area.Height);
        window.Width = Math.Min(window.Width, area.Width);
        window.Height = Math.Min(window.Height, area.Height);
    }
}
