using System.Text;
using System.Windows;
using System.Windows.Input;
using Piko.Desktop.Services;
using Piko.Runtime.Ipc;

namespace Piko.Desktop;

public partial class AgentWindow : Window
{
    private readonly RuntimeProcessManager _runtime;
    private readonly AppLogger _logger;
    private bool _sending;
    private RuntimeAgentPlanResponse? _lastPlan;

    public AgentWindow(RuntimeProcessManager runtime, AppLogger logger)
    {
        InitializeComponent();
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TranscriptText.Text = "Piko：我会结合当前情境回答。若 AI 未启用，请先在设置中配置。\n";
        Loaded += (_, _) => QuestionText.Focus();
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

    private async Task SendAsync()
    {
        var question = QuestionText.Text.Trim();
        if (_sending || string.IsNullOrWhiteSpace(question))
        {
            return;
        }

        _sending = true;
        SendButton.IsEnabled = false;
        QuestionText.IsEnabled = false;
        Append($"你：{question}\n");
        QuestionText.Clear();
        try
        {
            var result = await _runtime.PlanAgentAsync(question);
            _lastPlan = result;
            ExecuteButton.IsEnabled = result.ToolProposals.Any(proposal =>
                proposal.PermissionEnabled && proposal.Risk == "ReadOnly");
            Append(Format(result));
        }
        catch (Exception exception)
        {
            _logger.Error("Agent request failed", exception);
            Append("Piko：后台暂时无法处理这个请求。请检查“查看后台状态”和 AI 设置。\n");
        }
        finally
        {
            _sending = false;
            SendButton.IsEnabled = true;
            QuestionText.IsEnabled = true;
            QuestionText.Focus();
        }
    }

    private async void Execute_Click(object sender, RoutedEventArgs e)
    {
        var proposals = _lastPlan?.ToolProposals
            .Where(proposal => proposal.PermissionEnabled && proposal.Risk == "ReadOnly")
            .ToArray() ?? Array.Empty<RuntimeAgentToolProposal>();
        if (proposals.Length == 0)
        {
            return;
        }

        var folder = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择允许 Piko 只读访问的工作目录",
            Multiselect = false
        };
        if (folder.ShowDialog(this) != true)
        {
            return;
        }

        ExecuteButton.IsEnabled = false;
        try
        {
            foreach (var proposal in proposals)
            {
                var result = await _runtime.ExecuteReadAgentProposalAsync(
                    proposal.ProposalId,
                    folder.FolderName);
                Append(result.Success
                    ? $"工具结果（仅本地）：{result.Summary}\n{result.Output}\n"
                    : $"工具未执行成功：{result.Summary}\n");
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Read-only Agent execution failed", exception);
            Append("只读计划已失效、被拒绝或后台不可用。请重新提问生成计划。\n");
        }
        finally
        {
            _lastPlan = null;
        }
    }

    private static string Format(RuntimeAgentPlanResponse result)
    {
        if (!result.Available)
        {
            var reason = result.Reason switch
            {
                "cloud_ai_disabled" => "云端 AI 尚未启用。",
                "api_key_unavailable" => "Windows 凭据管理器中没有可用的 API Key。",
                "credential_unavailable" => "无法读取 Windows 凭据管理器。",
                "timeout" => "AI 请求超时。",
                _ => $"AI 当前不可用（{result.Reason}）。"
            };
            return $"Piko：{reason}\n";
        }

        var text = new StringBuilder();
        text.Append("Piko：").AppendLine(result.Message);
        foreach (var proposal in result.ToolProposals)
        {
            text.Append("计划（未执行）：")
                .Append(proposal.ToolName)
                .Append(" · ")
                .Append(proposal.Rationale)
                .Append(" · 风险 ")
                .AppendLine(proposal.Risk);
            if (proposal.Arguments.Count > 0)
            {
                text.Append("参数：")
                    .AppendLine(string.Join(", ", proposal.Arguments.Select(item => $"{item.Key}={item.Value}")));
            }
        }
        return text.ToString();
    }

    private void Append(string text)
    {
        TranscriptText.AppendText($"{text}\n");
        TranscriptText.ScrollToEnd();
    }
}
