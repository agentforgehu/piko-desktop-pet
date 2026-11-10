using System.Windows;
using Piko.Desktop.Services;

namespace Piko.Desktop;

public partial class MemoryWindow : Window
{
    private sealed record MemoryListItem(string Id, string Created, string Kind, string Sensitivity, string Summary);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private readonly RuntimeProcessManager _runtime;
    private readonly AppLogger _logger;

    public MemoryWindow(RuntimeProcessManager runtime, AppLogger logger)
    {
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void AddMemory_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryKindCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem selected ||
            selected.Tag is not string kind || string.IsNullOrWhiteSpace(MemorySummaryText.Text))
        {
            StatusText.Text = "先选择类型并填写要记住的摘要。";
            return;
        }

        try
        {
            await _runtime.AddMemoryAsync(kind, MemorySummaryText.Text.Trim());
            MemorySummaryText.Clear();
            StatusText.Text = "已加密保存这条记忆。";
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("Could not add a confirmed memory", exception);
            StatusText.Text = "保存失败，请检查记忆是否启用及后台状态。";
        }
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryList.SelectedItem is not MemoryListItem selected)
        {
            StatusText.Text = "先选择要删除的记忆。";
            return;
        }

        if (System.Windows.MessageBox.Show(
                $"确定删除这条记忆吗？\n\n{selected.Summary}",
                "删除选中记忆",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var deleted = await _runtime.DeleteMemoryAsync(selected.Id);
            StatusText.Text = deleted ? "已删除所选记忆。" : "这条记忆已不存在。";
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("Could not delete selected memory", exception);
            StatusText.Text = "删除失败，请检查后台状态。";
        }
    }

    private async void EditSelected_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryList.SelectedItem is not MemoryListItem selected)
        {
            StatusText.Text = "先选择要编辑的记忆。";
            return;
        }

        if (MemoryKindCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, selected.Kind, StringComparison.Ordinal)) is { } kindItem)
        {
            MemoryKindCombo.SelectedItem = kindItem;
        }

        if (string.IsNullOrWhiteSpace(MemorySummaryText.Text))
        {
            MemorySummaryText.Text = selected.Summary;
            StatusText.Text = "已载入所选记忆；编辑摘要后再次点击“更新选中项”。";
            return;
        }

        if (MemoryKindCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem chosen ||
            chosen.Tag is not string kind || MemorySummaryText.Text.Length > 240)
        {
            StatusText.Text = "请检查类型和摘要长度（最多 240 字符）。";
            return;
        }

        try
        {
            var updated = await _runtime.UpdateMemoryAsync(selected.Id, kind, MemorySummaryText.Text.Trim());
            StatusText.Text = updated ? "记忆已更新。" : "这条记忆已过期或不存在。";
            MemorySummaryText.Clear();
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("Could not update selected memory", exception);
            StatusText.Text = "更新失败，请检查记忆是否启用及后台状态。";
        }
    }

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

            MemoryList.ItemsSource = result.Items.Select(item => new MemoryListItem(
                item.Id,
                item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                item.Kind,
                item.Sensitivity,
                item.Summary)).ToArray();
            StatusText.Text = $"共 {result.Items.Count} 条。用户添加的偏好/语义记忆默认不过期；事件记忆按类型到期。";
        }
        catch (Exception exception)
        {
            _logger.Error("Could not list memories", exception);
            StatusText.Text = "读取失败，请检查后台状态。";
        }
    }
}
