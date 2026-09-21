using System.Windows;

namespace FourFoldAccountManager.Desktop.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog(bool fillGameToPanel, bool showFullScreenExitButton)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        FillOption.IsChecked = fillGameToPanel;
        FitOption.IsChecked = !fillGameToPanel;
        ShowFullScreenExitOption.IsChecked = showFullScreenExitButton;
    }

    public bool FillGameToPanel { get; private set; }

    public bool ShowFullScreenExitButton { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        FillGameToPanel = FillOption.IsChecked == true;
        ShowFullScreenExitButton = ShowFullScreenExitOption.IsChecked == true;
        DialogResult = true;
    }
}
