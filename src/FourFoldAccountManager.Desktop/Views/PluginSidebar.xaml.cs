using System.Collections;
using System.Windows;
using System.Windows.Controls;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Desktop.Views;

public partial class PluginSidebar : UserControl
{
    private AccountProfile? _selectedAccount;

    public PluginSidebar()
    {
        InitializeComponent();
        ActivePlugin = PluginKind.XpTracker;
        ClassComparisonPanelView.RefreshRequested += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        XpCalculatorPanelView.RefreshRequested += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        UpdateActivePlugin();
    }

    public PluginKind ActivePlugin { get; private set; }

    public Guid? SelectedAccountId { get; private set; }

    public event Action<Guid>? LinkRequested;
    public event Action<Guid>? ResetRateRequested;
    public event Action<Guid>? ResetAllRequested;
    public event EventHandler? RefreshRequested;

    public void SetTrackerItemsSource(IEnumerable? itemsSource) => TrackerPanel.ItemsSource = itemsSource;

    public void SetSelectedAccount(Guid? accountId)
    {
        SelectedAccountId = accountId;
        _selectedAccount = null;
        ClassComparisonPanelView.ClearSnapshot(accountId.HasValue
            ? "Select Refresh to load the selected profile." : "Select an account to load its profile.");
        XpCalculatorPanelView.ClearSnapshot(accountId.HasValue
            ? "Select Refresh to load the selected profile." : "Select an account to load its profile.");
    }

    public void SetSelectedAccount(AccountProfile? account)
    {
        SelectedAccountId = account?.Id;
        _selectedAccount = account;
        ClassComparisonPanelView.ClearSnapshot(account is null
            ? "Select an account to load its profile." : "Select Refresh to load the selected profile.");
        XpCalculatorPanelView.ClearSnapshot(account is null
            ? "Select an account to load its profile." : "Select Refresh to load the selected profile.");
    }

    public void SetProfileSnapshot(PlayerProgressSnapshot? snapshot)
    {
        if (snapshot is not null && _selectedAccount is not null)
        {
            ClassComparisonPanelView.SetSnapshot(_selectedAccount, snapshot);
            XpCalculatorPanelView.SetSnapshot(_selectedAccount, snapshot);
        }
    }

    public void SetProfileStatus(string status)
    {
        ClassComparisonPanelView.SetProfileStatus(status);
        XpCalculatorPanelView.SetProfileStatus(status);
    }

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
        ClassComparisonPanelView.Visibility = ActivePlugin == PluginKind.ClassComparison
            ? Visibility.Visible : Visibility.Collapsed;
        XpCalculatorPanelView.Visibility = ActivePlugin == PluginKind.XpCalculator
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
