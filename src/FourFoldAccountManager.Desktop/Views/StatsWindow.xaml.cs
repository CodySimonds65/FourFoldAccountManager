using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record StatsWindowChecklistItem(OverlayCardKey Key, string Label, bool InWindow);

// A plain window for overlay cards that the user can put on another monitor. Cards here are always movable,
// because there is no game underneath to click by accident.
public partial class StatsWindow : Window
{
    private const uint MonitorDefaultToNull = 0;

    private IReadOnlyList<StatsWindowChecklistItem> _checklist = [];
    private bool _maximizeOnOpen;
    private bool _closingForShutdown;

    public StatsWindow()
    {
        InitializeComponent();
        SourceInitialized += StatsWindow_SourceInitialized;
    }

    public event EventHandler<OverlayCardToggleRequestedEventArgs>? CardToggleRequested;

    // Raised with the window's final placement when the user closes it, but not when FourFold closes it on exit.
    public event EventHandler<StatsWindowPlacement>? ClosedByUser;

    public OverlayCardLayer Layer => CardLayer;

    public void SetCards(IReadOnlyList<OverlayCardModel> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        CardLayer.SetCards(cards, editing: true);
        EmptyText.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetChecklist(IReadOnlyList<StatsWindowChecklistItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _checklist = items;
    }

    // Call before Show. A null placement opens the window at its default size, centred on the main monitor.
    public void ApplyPlacement(StatsWindowPlacement? placement)
    {
        if (placement is null)
        {
            CenterOnMainMonitor();
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
        _maximizeOnOpen = placement.IsMaximized;
    }

    // A minimized window reports its restored spot and reopens restored.
    public StatsWindowPlacement CapturePlacement(bool isOpen)
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        return new StatsWindowPlacement(isOpen, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            WindowState == WindowState.Maximized);
    }

    public void CloseForShutdown()
    {
        _closingForShutdown = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel && !_closingForShutdown)
        {
            ClosedByUser?.Invoke(this, CapturePlacement(isOpen: false));
        }
    }

    private void StatsWindow_SourceInitialized(object? sender, EventArgs e)
    {
        WindowAppearance.Apply(this);
        // A window saved on a monitor that is no longer connected opens on the main monitor instead.
        if (MonitorFromWindow(new WindowInteropHelper(this).Handle, MonitorDefaultToNull) == 0)
        {
            CenterOnMainMonitor();
            _maximizeOnOpen = false;
        }

        if (_maximizeOnOpen)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void CenterOnMainMonitor()
    {
        var area = SystemParameters.WorkArea;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = StatsWindowPlacement.DefaultWidth;
        Height = StatsWindowPlacement.DefaultHeight;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    private void CardsButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = CardsButton, Placement = PlacementMode.Bottom };
        foreach (var item in _checklist)
        {
            // The themed MenuItem draws no check mark, so the tick is a CheckBox. Its content is a TextBlock so
            // an underscore in an account name is not read as an access key.
            var menuItem = new MenuItem
            {
                Header = new CheckBox
                {
                    Content = new TextBlock { Text = item.Label },
                    IsChecked = item.InWindow,
                    IsHitTestVisible = false,
                    Focusable = false
                }
            };
            var show = !item.InWindow;
            menuItem.Click += (_, _) =>
                CardToggleRequested?.Invoke(this, new OverlayCardToggleRequestedEventArgs(item.Key, show));
            menu.Items.Add(menuItem);
        }

        menu.IsOpen = true;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
}
