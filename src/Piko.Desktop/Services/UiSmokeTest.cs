using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Piko.Runtime.Ipc;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;

namespace Piko.Desktop.Services;

/// <summary>Renders actual WPF views without starting sensors, models, or an installed Runtime.</summary>
internal static class UiSmokeTest
{
    internal static void Run(string directory)
    {
        var paths = new AppPaths(Path.GetFullPath(directory));
        var logger = new AppLogger(paths);
        var evidence = new List<object>();
        var viewport = new Size();
        foreach (var size in new[] { new Size(500, 480), new Size(620, 620) })
        {
            viewport = size;
            Capture(new WelcomeWindow(), "welcome", ["StartButton"]);
            for (var tab = 0; tab < 4; tab++)
            {
                var settings = new SettingsWindow(new PikoSettings());
                ((TabControl)settings.FindName("SettingsTabs")).SelectedIndex = tab;
                Capture(settings, $"settings-{tab}", ["CancelButton", "TestButton", "SaveButton"]);
            }
            var agent = new AgentWindow(new RuntimeProcessManager(logger), logger);
            var question = (TextBox)agent.FindName("QuestionText");
            var send = (Button)agent.FindName("SendButton");
            if (send.IsEnabled) throw new InvalidOperationException("Empty questions must not be sendable.");
            question.Text = "陪我整理一下今天的思路。";
            if (!send.IsEnabled) throw new InvalidOperationException("Typed questions must be sendable.");
            ((Button)agent.FindName("ClearButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (question.Text.Length != 0 || send.IsEnabled)
                throw new InvalidOperationException("New conversation must reset the composer.");
            ((TextBlock)agent.FindName("StatusText")).Text = AgentInteractionText.DescribeFailure("model_disabled");
            Capture(agent, "conversation", ["SendButton", "ClearButton", "SettingsButton", "QuestionText"]);
        }
        File.WriteAllText(Path.Combine(directory, "ui-evidence.json"), JsonSerializer.Serialize(new
        {
            passed = true,
            kind = "WPF layout and raster export; not a physical mixed-DPI monitor test",
            captures = evidence
        }, new JsonSerializerOptions { WriteIndented = true }));

        void Capture(Window window, string name, string[] requiredControls)
        {
            // Detach the client area so no Loaded handler can initiate Runtime or network work.
            var content = (FrameworkElement)window.Content;
            var controls = requiredControls.Select(key => (FrameworkElement)window.FindName(key)).ToArray();
            window.Content = null;
            var frame = new Border { Background = window.Background, Child = content };
            TextElement.SetFontFamily(frame, window.FontFamily);
            TextElement.SetFontSize(frame, window.FontSize);
            TextElement.SetForeground(frame, window.Foreground);
            var size = viewport;
            frame.Measure(size);
            frame.Arrange(new Rect(size));
            frame.UpdateLayout();
            foreach (var control in controls)
            {
                var bounds = control.TransformToAncestor(frame).TransformBounds(new Rect(control.RenderSize));
                if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Left < -1 || bounds.Top < -1 ||
                    bounds.Right > size.Width + 1 || bounds.Bottom > size.Height + 1)
                    throw new InvalidOperationException($"{name}/{control.Name} is outside {size}: {bounds}");
            }
            foreach (var scale in new[] { 1d, 1.5, 2d })
            {
                var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale),
                    96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(frame);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var filename = $"{name}-{size.Width}x{size.Height}-{scale * 100:0}.png";
                using var stream = File.Create(Path.Combine(directory, filename));
                encoder.Save(stream);
                evidence.Add(new { filename, width = size.Width, height = size.Height, rasterScale = scale });
            }
            window.Close();
        }

    }
}
