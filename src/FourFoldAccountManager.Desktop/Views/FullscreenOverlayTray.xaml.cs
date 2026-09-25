using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record OverlayTraySwitch(
    OverlayCardKey Key,
    string Label,
    string Detail,
    bool IsEnabled,
    string AccessibleName);

public sealed record OverlayTrayAccountRow(
    Guid AccountId,
    string AccountLabel,
    IReadOnlyList<OverlayTraySwitch> Switches);

public sealed class OverlayCardToggleRequestedEventArgs(OverlayCardKey key, bool enabled) : EventArgs
{
    public OverlayCardKey Key { get; } = key;

    public bool Enabled { get; } = enabled;
}

public partial class FullscreenOverlayTray : UserControl
{
    private readonly FullscreenXpOverlayTabVisibilityState _tabVisibilityState = new();
    private bool _isFullScreen;
    private bool _isEditing;
    private bool _ignoreEdgeTabMouseEnterUntilLeave;

    public FullscreenOverlayTray()
    {
        InitializeComponent();
    }

    public event EventHandler? EditRequested;

    public event EventHandler? DoneRequested;

    public event EventHandler<OverlayCardToggleRequestedEventArgs>? CardToggleRequested;

    // Rows are rebuilt from saved settings after every toggle, so a failed save shows the persisted state.
    public void SetRows(IReadOnlyList<OverlayTraySwitch> globalSwitches, IReadOnlyList<OverlayTrayAccountRow> accountRows)
    {
        ArgumentNullException.ThrowIfNull(globalSwitches);
        ArgumentNullException.ThrowIfNull(accountRows);
        GlobalSwitchesControl.ItemsSource = globalSwitches.ToArray();
        GlobalSection.Visibility = globalSwitches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AccountRowsControl.ItemsSource = accountRows.ToArray();
    }

    public void SetFullscreen(bool isFullScreen)
    {
        _isFullScreen = isFullScreen;
        if (!isFullScreen)
        {
            _isEditing = false;
            TrayPanel.Visibility = Visibility.Collapsed;
        }

        Visibility = isFullScreen ? Visibility.Visible : Visibility.Collapsed;
        UpdateEdgeTabVisibility();
    }

    public void SetEditing(bool isEditing)
    {
        _isEditing = _isFullScreen && isEditing;
        TrayPanel.Visibility = _isEditing ? Visibility.Visible : Visibility.Collapsed;
    }

    public void DismissEdgeTab()
    {
        _tabVisibilityState.Dismiss();
        SetEditing(false);
        UpdateEdgeTabVisibility();
    }

    public void RevealEdgeTab()
    {
        var pointerWasAlreadyOverTab = _isFullScreen &&
            !_tabVisibilityState.IsVisible(_isFullScreen) && IsPointerOverEdgeTab();
        _tabVisibilityState.Reveal();
        _ignoreEdgeTabMouseEnterUntilLeave = pointerWasAlreadyOverTab;
        UpdateEdgeTabVisibility();
    }

    internal void RequestToggle(OverlayCardKey key, bool enabled)
    {
        if (!_isEditing)
        {
            return;
        }

        CardToggleRequested?.Invoke(this, new OverlayCardToggleRequestedEventArgs(key, enabled));
    }

    private void Switch_Click(object sender, RoutedEventArgs args)
    {
        if (sender is CheckBox { DataContext: OverlayTraySwitch item } checkBox)
        {
            RequestToggle(item.Key, checkBox.IsChecked == true);
        }
    }

    private void EdgeTab_Click(object sender, RoutedEventArgs args) => RequestEdit();

    private void EdgeTab_MouseEnter(object sender, MouseEventArgs args)
    {
        if (!_ignoreEdgeTabMouseEnterUntilLeave)
        {
            RequestEdit();
        }
    }

    private void EdgeTab_MouseLeave(object sender, MouseEventArgs args) =>
        _ignoreEdgeTabMouseEnterUntilLeave = false;

    private void DoneButton_Click(object sender, RoutedEventArgs args)
    {
        DismissEdgeTab();
        DoneRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateEdgeTabVisibility() =>
        EdgeTab.Visibility = _tabVisibilityState.IsVisible(_isFullScreen)
            ? Visibility.Visible
            : Visibility.Collapsed;

    private bool IsPointerOverEdgeTab()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return false;
        }

        var pointer = Mouse.GetPosition(this);
        var left = ActualWidth - EdgeTab.Width;
        var top = (ActualHeight - EdgeTab.Height) / 2d;
        return pointer.X >= left && pointer.X <= ActualWidth &&
            pointer.Y >= top && pointer.Y <= top + EdgeTab.Height;
    }

    private void RequestEdit()
    {
        if (!_isFullScreen || _isEditing)
        {
            return;
        }

        SetEditing(true);
        EditRequested?.Invoke(this, EventArgs.Empty);
    }
}
