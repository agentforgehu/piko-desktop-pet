using System.Windows;
using Piko.Desktop.Services;

namespace Piko.Desktop;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);
    }

    private void Start_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
