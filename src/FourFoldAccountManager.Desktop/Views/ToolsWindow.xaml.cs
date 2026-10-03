using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

// The pop-out home for the Account tools panel, which the user can put on another monitor. MainWindow moves its
// one panel in with Host and takes it back with Release.
public partial class ToolsWindow : Window
{
    private const uint MonitorDefaultToNull = 0;

    private bool _maximizeOnOpen;
    private bool _closingFromApp;

    public ToolsWindow()
    {
        InitializeComponent();
        SourceInitialized += ToolsWindow_SourceInitialized;
    }

    // Raised with the window's final placement when the user closes it, but not when FourFold closes it.
    public event EventHandler<ToolsWindowPlacement>? ClosedByUser;

    public void Host(FrameworkElement panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        PanelHost.Child = panel;
    }

    public FrameworkElement? Release()
    {
        var panel = PanelHost.Child as FrameworkElement;
        PanelHost.Child = null;
        return panel;
    }

    // Call before Show. A null placement opens the window at its default size, centred on the main monitor.
    public void ApplyPlacement(ToolsWindowPlacement? placement)
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
    public ToolsWindowPlacement CapturePlacement(bool isOpen)
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        return new ToolsWindowPlacement(isOpen, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            WindowState == WindowState.Maximized);
    }

    // Closes the window without raising ClosedByUser, for app exit and second-monitor mode changes.
    public void CloseFromApp()
    {
        _closingFromApp = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel && !_closingFromApp)
        {
            ClosedByUser?.Invoke(this, CapturePlacement(isOpen: false));
        }
    }

    private void ToolsWindow_SourceInitialized(object? sender, EventArgs e)
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
        Width = ToolsWindowPlacement.DefaultWidth;
        Height = ToolsWindowPlacement.DefaultHeight;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
}
