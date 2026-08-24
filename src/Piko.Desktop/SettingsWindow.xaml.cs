using System.Windows;
using Piko.Desktop.Services;

namespace Piko.Desktop;

public partial class SettingsWindow : Window
{
    private readonly PikoSettings _original;

    public SettingsWindow(PikoSettings settings)
    {
        InitializeComponent();
        _original = settings;
        AutonomousCheck.IsChecked = settings.AutonomousBehaviorEnabled;
        WindowCheck.IsChecked = settings.WindowExplorationEnabled;
        PointerCheck.IsChecked = settings.PointerAwarenessEnabled;
        FileCheck.IsChecked = settings.FileActivityAwarenessEnabled;
        MessagesCheck.IsChecked = settings.ShowMessages;
        ClickThroughCheck.IsChecked = settings.ClickThrough;
        StartupCheck.IsChecked = settings.LaunchAtStartup;
        DevelopmentCheck.IsChecked = settings.DevelopmentAwarenessEnabled;
        GitCheck.IsChecked = settings.GitAwarenessEnabled;
        AgentReadCheck.IsChecked = settings.AgentReadEnabled;
        MemoryCheck.IsChecked = settings.MemoryEnabled;
        CloudAiCheck.IsChecked = settings.CloudAiEnabled;
        AiEndpointText.Text = settings.AiEndpoint;
        AiModelText.Text = settings.AiModel;
    }

    public PikoSettings? Result { get; private set; }
    public string? ApiKeyUpdate { get; private set; }
    public bool ClearApiKey { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var candidate = _original with
        {
            AutonomousBehaviorEnabled = AutonomousCheck.IsChecked == true,
            WindowExplorationEnabled = WindowCheck.IsChecked == true,
            PointerAwarenessEnabled = PointerCheck.IsChecked == true,
            FileActivityAwarenessEnabled = FileCheck.IsChecked == true,
            ShowMessages = MessagesCheck.IsChecked == true,
            ClickThrough = ClickThroughCheck.IsChecked == true,
            LaunchAtStartup = StartupCheck.IsChecked == true,
            DevelopmentAwarenessEnabled = DevelopmentCheck.IsChecked == true,
            GitAwarenessEnabled = GitCheck.IsChecked == true,
            AgentReadEnabled = AgentReadCheck.IsChecked == true,
            MemoryEnabled = MemoryCheck.IsChecked == true,
            CloudAiEnabled = CloudAiCheck.IsChecked == true,
            AiEndpoint = AiEndpointText.Text.Trim(),
            AiModel = AiModelText.Text.Trim()
        };
        try
        {
            candidate.ToRuntimeUserSettings().Validate();
        }
        catch (ArgumentException exception)
        {
            System.Windows.MessageBox.Show(
                exception.Message,
                "Piko AI 设置",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Result = candidate;
        ApiKeyUpdate = string.IsNullOrWhiteSpace(AiApiKeyPassword.Password)
            ? null
            : AiApiKeyPassword.Password;
        ClearApiKey = ClearAiKeyCheck.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
