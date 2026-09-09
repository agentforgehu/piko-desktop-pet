using System.Windows;
using System.Windows.Input;
using Piko.Desktop.Services;
using Piko.Runtime;
using Piko.Runtime.Ipc;

namespace Piko.Desktop;

public partial class AgentWindow : Window
{
    private readonly RuntimeProcessManager _runtime;
    private readonly AppLogger _logger;
    private readonly Action<RuntimeAgentPlanResponse>? _onPetResponse;
    private readonly Action? _openSettings;
    private readonly AgentConversationHistory _history = new();
    private readonly List<ProposalOption> _proposals = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private bool _closed;

    public AgentWindow(RuntimeProcessManager runtime, AppLogger logger,
        Action<RuntimeAgentPlanResponse>? onPetResponse = null, Action? openSettings = null)
    {
        InitializeComponent();
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _onPetResponse = onPetResponse;
        _openSettings = openSettings;
        SettingsButton.IsEnabled = openSettings is not null;
        ShowWelcome();
        Loaded += async (_, _) =>
        {
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            QuestionText.Focus();
            await RefreshStatusAsync();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
            _history.Clear();
            _proposals.Clear();
            _lifetime.Dispose();
        };
    }

    private void ShowWelcome() => TranscriptText.Text =
        "Piko：今天想聊点什么？\n\n可以问我：\n“陪我整理一下今天的思路。”\n“我有点卡住了，一起想想下一步？”\n\n模型尚未开启时，可以从右上角进入设置。\n";

    private async Task RefreshStatusAsync()
    {
        try
        {
            var status = await _runtime.EnsureStartedAsync(_lifetime.Token);
            if (_closed || _operation is not null) return;
            StatusText.Text = status is null
                ? "后台暂未连接。桌面陪伴仍可使用，发送问题时会重试连接。"
                : status.ProviderMode == AiProviderMode.Disabled
                    ? AgentInteractionText.DescribeFailure("model_disabled")
                    : status.ModelHealth == "healthy" ? "模型已连接，可以开始聊天。"
                    : status.ModelHealth == "error" ? AgentInteractionText.DescribeFailure(status.ModelLastError)
                    : "模型已配置，发送第一条问题即可开始。";
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception exception)
        {
            _logger.Error("Could not read interaction status", exception);
            if (!_closed && _operation is null) StatusText.Text = "后台暂未连接，发送时会重新连接。";
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async void QuestionText_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private void QuestionText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CharacterCount is null || SendButton is null) return;
        CharacterCount.Text = $"{QuestionText.Text.Length} / {AgentConversationHistory.MaximumQuestionCharacters}";
        SendButton.IsEnabled = _operation is null && !string.IsNullOrWhiteSpace(QuestionText.Text);
    }

    private async Task SendAsync()
    {
        var question = QuestionText.Text.Trim();
        if (_closed || _operation is not null || string.IsNullOrWhiteSpace(question)) return;
        using var operation = BeginOperation("Piko 正在思考… 模型较慢时可能需要约一分钟，你可以随时停止。");
        _proposals.Clear();
        UpdatePlans();
        Append($"你：{question}");
        QuestionText.Clear();
        try
        {
            var result = await _runtime.PlanAgentAsync(_history.BuildRequest(question), operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (_closed) return;
            if (!result.Available)
            {
                RestoreQuestion(question, AgentInteractionText.DescribeFailure(result.Reason));
                return;
            }
            _history.Add(question, result.Message);
            Append($"Piko：{result.Message}");
            _proposals.AddRange(result.ToolProposals.Select(proposal => new ProposalOption(proposal)));
            UpdatePlans();
            StatusText.Text = _proposals.Count > 0
                ? "回复已完成。计划尚未执行；选择一项后确认访问目录。"
                : "回复已完成。";
            _onPetResponse?.Invoke(result);
        }
        catch (OperationCanceledException)
        {
            if (!_closed) RestoreQuestion(question, operation.IsCancellationRequested
                ? "已停止本次请求，问题已保留。" : AgentInteractionText.DescribeFailure("timeout"));
        }
        catch (Exception exception)
        {
            _logger.Error("Agent request failed", exception);
            if (!_closed) RestoreQuestion(question, AgentInteractionText.DescribeFailure(exception.Message));
        }
        finally { FinishOperation(operation); }
    }

    private async void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _operation is not null || PlanList.SelectedItem is not ProposalOption { CanExecute: true } option)
            return;
        using var operation = BeginOperation("请选择允许只读访问的目录。");
        try
        {
            var folder = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择允许 Piko 只读访问的工作目录", Multiselect = false
            };
            if (folder.ShowDialog(this) != true)
            {
                StatusText.Text = "未执行计划，可以重新选择。";
                return;
            }
            var confirmed = System.Windows.MessageBox.Show(this,
                $"操作：{option.Title}\n{option.Detail}\n\n允许访问：{folder.FolderName}\n\n结果仅在本地显示，不会自动发送给模型。执行这一项吗？",
                "确认只读访问", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (confirmed != MessageBoxResult.Yes)
            {
                StatusText.Text = "未执行计划，可以重新选择。";
                return;
            }
            _proposals.Remove(option); // One-shot proposal: never offer an ambiguous automatic retry.
            UpdatePlans();
            StatusText.Text = $"正在{option.Title}…";
            var result = await _runtime.ExecuteReadAgentProposalAsync(
                option.Proposal.ProposalId, folder.FolderName, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (_closed) return;
            Append(result.Success ? $"本地结果 · {option.Title}\n{result.Summary}\n{result.Output}"
                : $"计划未完成：{result.Summary}");
            if (result.WasTruncated) Append("结果较长，仅显示了限定范围内的内容。");
            StatusText.Text = result.Success ? "这一项已完成，结果仅在本地显示。" : "计划未完成，需要时请重新提问。";
        }
        catch (OperationCanceledException)
        {
            if (!_closed) StatusText.Text = "已停止等待这项计划。需要再次读取时，请重新提问生成计划。";
        }
        catch (Exception exception)
        {
            _logger.Error("Read-only Agent execution failed", exception);
            if (!_closed)
            {
                StatusText.Text = "计划已失效、后台忙碌或连接中断。请重新提问生成计划。";
                Append(StatusText.Text);
            }
        }
        finally { FinishOperation(operation); }
    }

    private CancellationTokenSource BeginOperation(string status)
    {
        var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        StatusText.Text = status;
        BusyProgress.Visibility = StopButton.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
        SendButton.IsEnabled = ExecuteButton.IsEnabled = ClearButton.IsEnabled = SettingsButton.IsEnabled = false;
        QuestionText.IsEnabled = PlanList.IsEnabled = false;
        return operation;
    }

    private void FinishOperation(CancellationTokenSource operation)
    {
        if (!ReferenceEquals(_operation, operation)) return;
        _operation = null;
        if (_closed) return;
        BusyProgress.Visibility = StopButton.Visibility = Visibility.Collapsed;
        QuestionText.IsEnabled = PlanList.IsEnabled = ClearButton.IsEnabled = true;
        SettingsButton.IsEnabled = _openSettings is not null;
        SendButton.IsEnabled = !string.IsNullOrWhiteSpace(QuestionText.Text);
        UpdateExecuteButton();
        if (IsActive) QuestionText.Focus();
    }

    private void RestoreQuestion(string question, string message)
    {
        QuestionText.Text = question;
        RetryButton.Visibility = Visibility.Visible;
        StatusText.Text = message;
        Append($"Piko：{message}");
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "正在停止本次请求…";
        _operation?.Cancel();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null) return;
        _history.Clear();
        _proposals.Clear();
        UpdatePlans();
        QuestionText.Clear();
        RetryButton.Visibility = Visibility.Collapsed;
        ShowWelcome();
        StatusText.Text = "已开始新对话，上一段对话不会继续发送。";
        QuestionText.Focus();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is null) _openSettings?.Invoke();
    }

    private void UpdatePlans()
    {
        PlanList.ItemsSource = _proposals.ToArray();
        PlanPanel.Visibility = _proposals.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateExecuteButton();
    }

    private void PlanList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateExecuteButton();
    private void UpdateExecuteButton()
    {
        if (ExecuteButton is not null)
            ExecuteButton.IsEnabled = _operation is null && PlanList.SelectedItem is ProposalOption { CanExecute: true };
    }

    private void Append(string text)
    {
        TranscriptText.AppendText($"\n{text}\n");
        // Keep the local display bounded during long sessions as well as the model history.
        if (TranscriptText.Text.Length > 131_072) TranscriptText.Text = TranscriptText.Text[^98_304..];
        TranscriptText.ScrollToEnd();
    }

    public sealed record ProposalOption(RuntimeAgentToolProposal Proposal)
    {
        public bool CanExecute => Proposal.PermissionEnabled && Proposal.Risk == "ReadOnly";
        public string Title => AgentInteractionText.ToolTitle(Proposal.ToolName);
        public string Detail => Proposal.Rationale +
            (Proposal.Arguments.Count > 0 ? "\n" + string.Join(" · ", Proposal.Arguments.Select(item => $"{item.Key}: {item.Value}")) : "") +
            (CanExecute ? "" : "\n只读权限尚未开启，请先到设置中开启并重新提问。");
    }
}
