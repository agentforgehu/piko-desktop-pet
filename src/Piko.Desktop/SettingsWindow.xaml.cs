using System.Windows;
using Piko.Desktop.Services;
using Piko.Runtime;

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
        ForegroundActivityCheck.IsChecked = settings.ForegroundActivityAwarenessEnabled;
        ProviderCombo.SelectedIndex = (int)settings.ProviderMode;
        AiEndpointText.Text = settings.AiEndpoint;
        AiModelText.Text = settings.AiModel;
        LocalAiEndpointText.Text = settings.LocalAiEndpoint;
        LocalAiModelText.Text = settings.LocalAiModel;
        AddressModeCombo.SelectedIndex = (int)settings.UserAddressMode;
        UserNameText.Text = settings.UserName;
        CustomAddressText.Text = settings.CustomAddress;
        PersonalityText.Text = settings.Personality;
        ProactivityCombo.SelectedIndex = (int)settings.Proactivity;
    }

    public PikoSettings? Result { get; private set; }
    public string? ApiKeyUpdate { get; private set; }
    public bool ClearApiKey { get; private set; }
    public bool TestConnectionRequested { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e) => TryFinish(testConnection: false);

    private void SaveAndTest_Click(object sender, RoutedEventArgs e) => TryFinish(testConnection: true);

    private void TryFinish(bool testConnection)
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
            CloudAiEnabled = ProviderCombo.SelectedIndex == (int)AiProviderMode.OpenAiApi,
            ProviderMode = (AiProviderMode)Math.Max(0, ProviderCombo.SelectedIndex),
            AiEndpoint = AiEndpointText.Text.Trim(),
            AiModel = AiModelText.Text.Trim(),
            LocalAiEndpoint = LocalAiEndpointText.Text.Trim(),
            LocalAiModel = LocalAiModelText.Text.Trim(),
            UserAddressMode = (UserAddressMode)Math.Max(0, AddressModeCombo.SelectedIndex),
            UserName = UserNameText.Text.Trim(),
            CustomAddress = CustomAddressText.Text.Trim(),
            Personality = PersonalityText.Text.Trim(),
            Proactivity = (PetProactivity)Math.Max(0, ProactivityCombo.SelectedIndex),
            ForegroundActivityAwarenessEnabled = ForegroundActivityCheck.IsChecked == true
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
        TestConnectionRequested = testConnection;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

