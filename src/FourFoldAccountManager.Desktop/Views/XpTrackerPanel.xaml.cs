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

    private void LinkPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid accountId }) LinkRequested?.Invoke(accountId);
    }
}
