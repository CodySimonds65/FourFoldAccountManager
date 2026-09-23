using System.Collections;
using System.Windows;
using System.Windows.Controls;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Desktop.Views;

public partial class PluginSidebar : UserControl
{
    public PluginSidebar()
    {
        InitializeComponent();
        ActivePlugin = PluginKind.XpTracker;
        UpdateActivePlugin();
    }

    public PluginKind ActivePlugin { get; private set; }

    public Guid? SelectedAccountId { get; private set; }

    public event Action<Guid>? LinkRequested;
    public event Action<Guid>? ResetRateRequested;
    public event Action<Guid>? ResetAllRequested;
    public event EventHandler? RefreshRequested;

    public void SetTrackerItemsSource(IEnumerable? itemsSource) => TrackerPanel.ItemsSource = itemsSource;

    public void SetSelectedAccount(Guid? accountId) => SelectedAccountId = accountId;

    public void SetProfileSnapshot(PlayerProgressSnapshot? snapshot)
    {
        ProfileStatusText.Text = snapshot is null ? string.Empty : $"Profile read: {snapshot.Username}";
    }

    public void SetProfileStatus(string status) => ProfileStatusText.Text = status;

    public void ShowPlugin(PluginKind plugin)
    {
        ActivePlugin = FourFoldAccountManager.Core.Panel.PluginSelectionPolicy.Normalize(plugin);
        UpdateActivePlugin();
    }

    private void PluginButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PluginKind plugin })
        {
            ShowPlugin(plugin);
            if (plugin is PluginKind.ClassComparison or PluginKind.XpCalculator)
            {
                RefreshRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void UpdateActivePlugin()
    {
        TrackerPanel.Visibility = ActivePlugin == PluginKind.XpTracker ? Visibility.Visible : Visibility.Collapsed;
        ClassComparisonPlaceholder.Visibility = ActivePlugin == PluginKind.ClassComparison
            ? Visibility.Visible : Visibility.Collapsed;
        XpCalculatorPlaceholder.Visibility = ActivePlugin == PluginKind.XpCalculator
            ? Visibility.Visible : Visibility.Collapsed;

        XpTrackerButton.Opacity = ActivePlugin == PluginKind.XpTracker ? 1d : 0.65d;
        ClassComparisonButton.Opacity = ActivePlugin == PluginKind.ClassComparison ? 1d : 0.65d;
        XpCalculatorButton.Opacity = ActivePlugin == PluginKind.XpCalculator ? 1d : 0.65d;
    }

    private void TrackerPanel_Loaded(object sender, RoutedEventArgs e)
    {
        TrackerPanel.LinkRequested += accountId => LinkRequested?.Invoke(accountId);
        TrackerPanel.ResetRateRequested += accountId => ResetRateRequested?.Invoke(accountId);
        TrackerPanel.ResetAllRequested += accountId => ResetAllRequested?.Invoke(accountId);
    }
}
