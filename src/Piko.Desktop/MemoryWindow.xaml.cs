using System.Windows;
using Piko.Desktop.Services;

namespace Piko.Desktop;

public partial class MemoryWindow : Window
{
    private sealed record MemoryListItem(string Id, DateTimeOffset UpdatedAt, string Created, string Kind, string Sensitivity, string Provenance, string Status, bool IsSuperseded, string Summary);

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
            var summary = MemorySummaryText.Text.Trim();
            var related = await _runtime.FindRelatedMemoriesAsync(kind, summary);
            if (!related.Available)
            {
                StatusText.Text = related.Reason == "memory_disabled"
                    ? "本地记忆尚未启用。"
                    : "本地记忆暂时不可用。";
                return;
            }

            if (related.Items.Count > 0)
            {
                var closest = related.Items[0];
                var options = string.Join("\n", related.Items.Take(3).Select(item => $"• {item.Summary}"));
                var choice = System.Windows.MessageBox.Show(
                    $"发现相似的用户确认记忆：\n\n{options}\n\n“是”将最相似的一条保存为新版本；“否”将这条记忆单独保存；“取消”放弃操作。系统只判断文字相似，不判断事实是否矛盾。",
                    "检查相关记忆",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question,
                    MessageBoxResult.Cancel);
                if (choice == MessageBoxResult.Cancel)
                {
                    StatusText.Text = "已取消保存。";
                    return;
                }
                if (choice == MessageBoxResult.Yes)
                {
                    var replaced = await _runtime.ReplaceMemoryAsync(
                        closest.Id, closest.UpdatedAt, kind, summary);
                    if (!replaced)
                    {
                        StatusText.Text = "相关记忆已被修改或删除，请刷新后重试。";
                        await RefreshAsync();
                        return;
                    }
                    StatusText.Text = "已保存为新版本，旧版本保留在历史中。";
                }
                else
                {
                    await _runtime.AddMemoryAsync(kind, summary);
                    StatusText.Text = "已将相关内容作为独立记忆保存。";
                }
            }
            else
            {
                await _runtime.AddMemoryAsync(kind, summary);
                StatusText.Text = "已加密保存这条记忆。";
            }
            MemorySummaryText.Clear();
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

        if (selected.IsSuperseded)
        {
            StatusText.Text = "该版本已归档，请选中当前版本。";
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
            StatusText.Text = "已载入当前版本；编辑摘要后再次点击“保存为新版本”，旧版本会保留为历史记录。";
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
            var replaced = await _runtime.ReplaceMemoryAsync(selected.Id, selected.UpdatedAt, kind, MemorySummaryText.Text.Trim());
            StatusText.Text = replaced ? "已保存为新版本，旧版本已归档。" : "这条记忆已被修改、过期或删除，请刷新后重试。";
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
                item.UpdatedAt,
                item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                item.Kind,
                item.Sensitivity,
                item.Provenance,
                item.IsSuperseded ? "已被替代" : "当前",
                item.IsSuperseded,
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
