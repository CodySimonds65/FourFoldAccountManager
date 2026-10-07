using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace FourFoldAccountManager.Desktop.Views;

public partial class XpTrackerPanel : UserControl
{
    public XpTrackerPanel() => InitializeComponent();

    public IEnumerable? ItemsSource
    {
        get => RowsControl.ItemsSource;
        set => RowsControl.ItemsSource = value;
    }

    public event Action<Guid>? LinkRequested;
    public event Action<Guid>? ResetRateRequested;
    public event Action<Guid>? ResetAllRequested;

    private void LinkPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid accountId }) LinkRequested?.Invoke(accountId);
    }

    private void ResetRate_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetAccountId(sender, out var accountId)) ResetRateRequested?.Invoke(accountId);
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetAccountId(sender, out var accountId)) ResetAllRequested?.Invoke(accountId);
    }

    // The rows are rebuilt every second while an account is live, which disconnects an open menu from its row. So the
    // account is taken when the menu opens, not when an item is clicked.
    private void RowMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement { DataContext: XpTrackerRow row } } menu)
        {
            menu.Tag = row.AccountId;
        }
    }

    private static bool TryGetAccountId(object sender, out Guid accountId)
    {
        if (sender is MenuItem { Parent: ContextMenu { Tag: Guid id } })
        {
            accountId = id;
            return true;
        }

        accountId = default;
        return false;
    }
}
