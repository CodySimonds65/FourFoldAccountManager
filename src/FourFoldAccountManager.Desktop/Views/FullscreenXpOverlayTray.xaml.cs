using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FourFoldAccountManager.Desktop.Views;

public partial class FullscreenXpOverlayTray : UserControl
{
    private readonly FullscreenXpOverlayTabVisibilityState _tabVisibilityState = new();
    private Point? _dragStartPoint;
    private XpOverlayAccountChoice? _dragChoice;
    private bool _isFullScreen;
    private bool _isEditing;
    private bool _ignoreEdgeTabMouseEnterUntilLeave;

    public FullscreenXpOverlayTray()
    {
        InitializeComponent();
    }

    public event EventHandler? EditRequested;

    public event EventHandler? DoneRequested;

    public void SetChoices(IReadOnlyList<XpOverlayAccountChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ChoicesControl.ItemsSource = choices.ToArray();
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

    private void Choice_MouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: XpOverlayAccountChoice choice })
        {
            _dragChoice = choice;
            _dragStartPoint = args.GetPosition(this);
        }
    }

    private void Choice_MouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        _dragChoice = null;
        _dragStartPoint = null;
    }

    private void Choice_MouseMove(object sender, MouseEventArgs args)
    {
        if (!_isEditing || _dragChoice is not { } choice || _dragStartPoint is not { } start ||
            args.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement source)
        {
            return;
        }

        var current = args.GetPosition(this);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(XpOverlayLayer.AccountDragDataFormat, choice.AccountId);
        _dragChoice = null;
        _dragStartPoint = null;
        DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
    }
}
