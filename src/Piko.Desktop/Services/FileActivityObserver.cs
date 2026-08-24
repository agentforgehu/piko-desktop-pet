using System.IO;
using System.Windows.Automation;
using Piko.World.Behavior;
using Piko.World.Model;

namespace Piko.Desktop.Services;

public sealed class FileActivityObserver : IDisposable
{
    private static readonly HashSet<string> TemporaryDownloadExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".crdownload", ".part", ".download", ".tmp" };

    private readonly List<FileSystemWatcher> _watchers = new();
    private DateTimeOffset _lastActivity;
    private FileActivityConfidence _confidence;
    private string _source = "none";
    private double? _progress;

    public FileActivityObserver(AppLogger logger)
    {
        var folders = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        foreach (var folder in folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
                };
                watcher.Created += OnChanged;
                watcher.Changed += OnChanged;
                watcher.Renamed += OnRenamed;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception exception)
            {
                logger.Error("Could not start a file activity observer", exception);
            }
        }
    }

    public void UpdateDesktop(DesktopSnapshot snapshot)
    {
        foreach (var window in snapshot.Windows.Where(window =>
                     window.IsVisible &&
                     window.ClassName == "OperationStatusWindow" &&
                     !window.IsMinimized))
        {
            if (!long.TryParse(window.Id, System.Globalization.NumberStyles.HexNumber, null, out var handleValue))
            {
                continue;
            }

            try
            {
                var root = AutomationElement.FromHandle(new nint(handleValue));
                var progressElement = root.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.ProgressBar));
                if (progressElement is null)
                {
                    continue;
                }

                _progress = null;
                _confidence = FileActivityConfidence.ActivityOnly;
                if (progressElement.TryGetCurrentPattern(
                        RangeValuePattern.Pattern,
                        out var pattern) && pattern is RangeValuePattern range &&
                    range.Current.Maximum > range.Current.Minimum)
                {
                    _progress = Math.Clamp(
                        (range.Current.Value - range.Current.Minimum) /
                        (range.Current.Maximum - range.Current.Minimum),
                        0,
                        1);
                    _confidence = FileActivityConfidence.Exact;
                }

                _lastActivity = DateTimeOffset.UtcNow;
                _source = "shell_progress_control";
                return;
            }
            catch (ElementNotAvailableException)
            {
                // The operation window closed between enumeration and inspection.
            }
            catch (InvalidOperationException)
            {
                // The target does not expose a usable automation tree.
            }
        }
    }

    public FileActivitySignal Current
    {
        get
        {
            var active = DateTimeOffset.UtcNow - _lastActivity < TimeSpan.FromSeconds(6);
            return active
                ? new FileActivitySignal(true, _confidence, _progress, _source)
                : FileActivitySignal.None;
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Record(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e) => Record(e.FullPath);

    private void Record(string path)
    {
        _lastActivity = DateTimeOffset.UtcNow;
        _confidence = TemporaryDownloadExtensions.Contains(Path.GetExtension(path))
            ? FileActivityConfidence.Estimated
            : FileActivityConfidence.ActivityOnly;
        _progress = null;
        _source = "watched_user_folder";
    }
}
