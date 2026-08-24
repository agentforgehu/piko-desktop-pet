namespace Piko.Desktop.Services;

public sealed class AppLogger
{
    private const long MaximumLogBytes = 1024 * 1024;
    private readonly string _path;
    private readonly object _sync = new();

    public AppLogger(AppPaths paths)
    {
        _path = paths.LogFile;
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > MaximumLogBytes)
                {
                    File.Move(_path, _path + ".previous", true);
                }

                File.AppendAllText(
                    _path,
                    $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never take the pet down.
            }
        }
    }
}
