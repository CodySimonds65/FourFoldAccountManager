using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Desktop.Views;

public sealed class FloatingCardBoundsCommittedEventArgs(OverlayCardKey key, FloatingCardBounds bounds) : EventArgs
{
    public OverlayCardKey Key { get; } = key;

    public FloatingCardBounds Bounds { get; } = bounds;
}

// One floating overlay card: a borderless window that stays on top of every app, never takes focus, and stays out
// of the taskbar and Alt+Tab. Outside arrange mode Windows passes clicks straight through it.
public partial class FloatingCardWindow : Window
{
    private const int ExtendedStyleIndex = -20; // GWL_EXSTYLE
    private const long TransparentStyle = 0x20; // WS_EX_TRANSPARENT
    private const long ToolWindowStyle = 0x80; // WS_EX_TOOLWINDOW
    private const long NoActivateStyle = 0x08000000; // WS_EX_NOACTIVATE
    private const uint MonitorDefaultToNull = 0;
    private const double CascadeOrigin = 24;

    private readonly OverlayAddOnDefinition _definition;
    private readonly int _cascadeIndex;
    private bool _arranging;

    public FloatingCardWindow(OverlayCardKey key, OverlayAddOnDefinition definition, int cascadeIndex)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Key = key;
        _definition = definition;
        _cascadeIndex = cascadeIndex;
        InitializeComponent();
        MinWidth = definition.MinimumWidth;
        MinHeight = definition.MinimumHeight;
        Frame.MoveDelta += (_, args) =>
        {
            Left += args.HorizontalChange;
            Top += args.VerticalChange;
        };
        Frame.ResizeDelta += (_, args) =>
        {
            Width = Math.Max(MinWidth, Width + args.HorizontalChange);
            Height = Math.Max(MinHeight, Height + args.VerticalChange);
        };
        Frame.MoveCompleted += OnManipulationCompleted;
        Frame.ResizeCompleted += OnManipulationCompleted;
        SourceInitialized += FloatingCardWindow_SourceInitialized;
    }

    public OverlayCardKey Key { get; }

    public event EventHandler<FloatingCardBoundsCommittedEventArgs>? BoundsCommitted;

    public void SetCard(object data, bool arranging)
    {
        Frame.CardData = data;
        Frame.IsEditing = arranging;
        _arranging = arranging;
        ApplyClickThrough();
    }

    // Puts the window at a saved spot, or at its cascade spot on the main monitor when there is no usable one.
    // Used before the first Show and to snap back after a position could not be saved.
    public void Place(FloatingCardBounds? bounds)
    {
        if (bounds is not { IsUsable: true })
        {
            PlaceAtDefault();
            return;
        }

        Left = bounds.Left;
        Top = bounds.Top;
        Width = Math.Max(MinWidth, bounds.Width);
        Height = Math.Max(MinHeight, bounds.Height);
    }

    private void PlaceAtDefault()
    {
        var area = SystemParameters.WorkArea;
        var offset = CascadeOrigin + _cascadeIndex * OverlayCardPolicy.CascadeStep;
        Width = _definition.DefaultWidth;
        Height = _definition.DefaultHeight;
        Left = area.Left + offset;
        Top = area.Top + offset;
    }

    private void FloatingCardWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetExtendedStyle(handle, GetExtendedStyle(handle) | ToolWindowStyle | NoActivateStyle);
        ApplyClickThrough();
        // A card saved on a monitor that is no longer connected moves to its spot on the main monitor.
        if (MonitorFromWindow(handle, MonitorDefaultToNull) == 0)
        {
            PlaceAtDefault();
        }
    }

    private void ApplyClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
        {
            return;
        }

        var style = GetExtendedStyle(handle);
        SetExtendedStyle(handle, _arranging ? style & ~TransparentStyle : style | TransparentStyle);
    }

    private void OnManipulationCompleted(object sender, DragCompletedEventArgs args) =>
        BoundsCommitted?.Invoke(this, new FloatingCardBoundsCommittedEventArgs(
            Key, new FloatingCardBounds(Left, Top, ActualWidth, ActualHeight)));

    private static long GetExtendedStyle(nint window) =>
        nint.Size == 8
            ? GetWindowLongPtr(window, ExtendedStyleIndex)
            : GetWindowLong(window, ExtendedStyleIndex);

    private static void SetExtendedStyle(nint window, long style)
    {
        if (nint.Size == 8)
        {
            SetWindowLongPtr(window, ExtendedStyleIndex, (nint)style);
        }
        else
        {
            SetWindowLong(window, ExtendedStyleIndex, (int)style);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint window, int index, int value);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
}
