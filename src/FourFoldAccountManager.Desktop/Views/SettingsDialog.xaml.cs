using System.Windows;

namespace FourFoldAccountManager.Desktop.Views;

public partial class SettingsDialog : Window
{
    private readonly Func<MessageBoxResult>? _confirmResetLayoutSizes;

    public SettingsDialog(bool fillGameToPanel, bool showFullScreenExitButton) :
        this(fillGameToPanel, showFullScreenExitButton, null)
    {
    }

    internal SettingsDialog(
        bool fillGameToPanel,
        bool showFullScreenExitButton,
        Func<MessageBoxResult>? confirmResetLayoutSizes)
    {
        _confirmResetLayoutSizes = confirmResetLayoutSizes;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        FillOption.IsChecked = fillGameToPanel;
        FitOption.IsChecked = !fillGameToPanel;
        ShowFullScreenExitOption.IsChecked = showFullScreenExitButton;
    }

    public bool FillGameToPanel { get; private set; }

    public bool ShowFullScreenExitButton { get; private set; }

    public bool ResetLayoutSizes { get; private set; }

    private void ResetLayoutSizes_Click(object sender, RoutedEventArgs e)
    {
        var result = _confirmResetLayoutSizes?.Invoke() ?? MessageBox.Show(
            this,
            "Restore all client layout dividers to their default positions? Game scaling and per-client viewport sizes will not change.",
            "Reset layout sizes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            ResetLayoutSizes = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        FillGameToPanel = FillOption.IsChecked == true;
        ShowFullScreenExitButton = ShowFullScreenExitOption.IsChecked == true;
        DialogResult = true;
    }
}
