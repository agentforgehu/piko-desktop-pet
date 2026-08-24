using System.Windows;
using Piko.Desktop.Services;

namespace Piko.Desktop;

public partial class MemoryWindow : Window
{
    private readonly RuntimeProcessManager _runtime;
    private readonly AppLogger _logger;

    public MemoryWindow(RuntimeProcessManager runtime, AppLogger logger)
    {
        InitializeComponent();
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void DeleteAll_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(
                "确定删除 Piko 的全部本地记忆吗？此操作不能撤销。",
                "删除全部记忆",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var deleted = await _runtime.DeleteAllMemoriesAsync();
            StatusText.Text = $"已删除 {deleted} 条记忆。";
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("Could not delete memories", exception);
            StatusText.Text = "删除失败，请检查后台状态。";
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var result = await _runtime.ListMemoriesAsync();
            if (!result.Available)
            {
                MemoryList.ItemsSource = null;
                StatusText.Text = result.Reason == "memory_disabled"
                    ? "本地记忆尚未启用，可在设置中开启。"
                    : "本地记忆暂时不可用。";
                return;
            }

            MemoryList.ItemsSource = result.Items.Select(item => new
            {
                Created = item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                Kind = item.Kind,
                Summary = item.Summary
            }).ToArray();
            StatusText.Text = $"共 {result.Items.Count} 条；Working 默认 1 天，Episodic 默认 30 天。";
        }
        catch (Exception exception)
        {
            _logger.Error("Could not list memories", exception);
            StatusText.Text = "读取失败，请检查后台状态。";
        }
    }
}
