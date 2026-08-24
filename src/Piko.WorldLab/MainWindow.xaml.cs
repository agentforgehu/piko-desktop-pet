using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Piko.World.Compiler;
using Piko.World.Geometry;
using Piko.World.Model;
using Piko.World.Serialization;
using Piko.World.Windows.Observation;

namespace Piko.WorldLab;

public partial class MainWindow : Window
{
    private readonly WindowsSnapshotProvider _snapshotProvider = new();
    private readonly DesktopWorldCompiler _compiler = new();
    private DesktopWorld? _world;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var snapshot = _snapshotProvider.Capture();
            _world = _compiler.Compile(snapshot);
            StatusText.Text = $"Captured {snapshot.Monitors.Count} monitor(s), " +
                              $"{snapshot.Windows.Count} window(s), " +
                              $"{_world.Surfaces.Count} surface(s).";
            RenderWorld();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Capture failed: {exception.Message}";
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_world is null)
        {
            StatusText.Text = "Capture a world before exporting.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export privacy-conscious desktop snapshot",
            Filter = "Piko world snapshot (*.json)|*.json",
            FileName = $"piko-world-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, DesktopSnapshotJson.Serialize(_world.Source));
        StatusText.Text = $"Snapshot exported to {dialog.FileName}";
    }

    private void WorldCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderWorld();

    private void RenderWorld()
    {
        WorldCanvas.Children.Clear();
        SurfaceList.Items.Clear();

        if (_world is null || WorldCanvas.ActualWidth <= 0 || WorldCanvas.ActualHeight <= 0)
        {
            return;
        }

        var desktop = _world.Source.VirtualDesktop;
        if (desktop.IsEmpty)
        {
            return;
        }

        const double padding = 24;
        var scale = Math.Min(
            Math.Max(1, WorldCanvas.ActualWidth - padding * 2) / desktop.Width,
            Math.Max(1, WorldCanvas.ActualHeight - padding * 2) / desktop.Height);

        var offsetX = padding - desktop.Left * scale;
        var offsetY = padding - desktop.Top * scale;

        foreach (var monitor in _world.Source.Monitors)
        {
            AddRectangle(monitor.Bounds, scale, offsetX, offsetY, Brushes.Transparent, Brushes.DimGray, 2);
        }

        foreach (var window in _world.Source.Windows.OrderByDescending(window => window.ZOrder))
        {
            var fill = window.IsEligible
                ? new SolidColorBrush(Color.FromArgb(50, 61, 184, 118))
                : new SolidColorBrush(Color.FromArgb(28, 220, 76, 90));
            var stroke = window.IsEligible ? Brushes.SeaGreen : Brushes.IndianRed;
            AddRectangle(window.Bounds, scale, offsetX, offsetY, fill, stroke, 1);
        }

        foreach (var surface in _world.Surfaces)
        {
            var line = new Line
            {
                X1 = offsetX + surface.Horizontal.Start * scale,
                X2 = offsetX + surface.Horizontal.End * scale,
                Y1 = offsetY + surface.Y * scale,
                Y2 = offsetY + surface.Y * scale,
                Stroke = surface.Kind == SurfaceKind.WindowTop ? Brushes.DeepSkyBlue : Brushes.Gold,
                StrokeThickness = surface.Kind == SurfaceKind.WindowTop ? 3 : 2
            };
            WorldCanvas.Children.Add(line);
            SurfaceList.Items.Add(
                $"{surface.Kind,-12} y={surface.Y,6:0} " +
                $"x={surface.Horizontal.Start,6:0}..{surface.Horizontal.End,6:0} " +
                $"owner={surface.OwnerWindowId ?? "monitor"}");
        }

        var cursor = _world.Source.Cursor;
        var cursorDot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Brushes.White
        };
        Canvas.SetLeft(cursorDot, offsetX + cursor.X * scale - 4);
        Canvas.SetTop(cursorDot, offsetY + cursor.Y * scale - 4);
        WorldCanvas.Children.Add(cursorDot);
    }

    private void AddRectangle(
        PixelRect bounds,
        double scale,
        double offsetX,
        double offsetY,
        Brush fill,
        Brush stroke,
        double strokeThickness)
    {
        if (bounds.IsEmpty)
        {
            return;
        }

        var rectangle = new Rectangle
        {
            Width = bounds.Width * scale,
            Height = bounds.Height * scale,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeThickness
        };
        Canvas.SetLeft(rectangle, offsetX + bounds.Left * scale);
        Canvas.SetTop(rectangle, offsetY + bounds.Top * scale);
        WorldCanvas.Children.Add(rectangle);
    }
}
