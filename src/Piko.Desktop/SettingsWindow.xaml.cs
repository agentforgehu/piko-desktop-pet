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
    }

    public PikoSettings? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = _original with
        {
            AutonomousBehaviorEnabled = AutonomousCheck.IsChecked == true,
            WindowExplorationEnabled = WindowCheck.IsChecked == true,
            PointerAwarenessEnabled = PointerCheck.IsChecked == true,
            FileActivityAwarenessEnabled = FileCheck.IsChecked == true,
            ShowMessages = MessagesCheck.IsChecked == true,
            ClickThrough = ClickThroughCheck.IsChecked == true,
            LaunchAtStartup = StartupCheck.IsChecked == true
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
