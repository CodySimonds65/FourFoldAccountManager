using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

public partial class SettingsDialog : Window
{

    private readonly IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> _initialShortcuts;
    private readonly IReadOnlySet<GlobalShortcutAction> _unavailableShortcuts;
    private readonly Dictionary<GlobalShortcutAction, GlobalHotkeyChord> _shortcuts;
    private readonly IReadOnlyDictionary<GlobalShortcutAction, ShortcutRow> _rows;
    private GlobalShortcutAction? _capturingShortcut;

    internal SettingsDialog(
        bool fillGameToPanel,
        bool showFullScreenExitButton,
        IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> shortcuts,
        IReadOnlySet<GlobalShortcutAction> unavailableShortcuts,
        bool showOverlaysInTheatreMode,
        SecondMonitorMode secondMonitorMode,
        bool blockStorePages)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(unavailableShortcuts);
        if (GlobalShortcutActions.All.Any(action => !shortcuts.ContainsKey(action)))
        {
            throw new ArgumentException("Every shortcut action needs a chord.", nameof(shortcuts));
        }

        _initialShortcuts = new Dictionary<GlobalShortcutAction, GlobalHotkeyChord>(shortcuts);
        _shortcuts = new Dictionary<GlobalShortcutAction, GlobalHotkeyChord>(shortcuts);
        _unavailableShortcuts = unavailableShortcuts;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        FillOption.IsChecked = fillGameToPanel;
        FitOption.IsChecked = !fillGameToPanel;
        ShowFullScreenExitOption.IsChecked = showFullScreenExitButton;
        ShowOverlaysInTheatreModeOption.IsChecked = showOverlaysInTheatreMode;
        AccountToolsModeOption.IsChecked = secondMonitorMode == SecondMonitorMode.AccountToolsWindow;
        FloatingCardsModeOption.IsChecked = secondMonitorMode == SecondMonitorMode.FloatingCards;
        BlockStorePagesOption.IsChecked = blockStorePages;
        _rows = new[]
        {
            RevealShortcutRow, DividerShortcutRow, NextTabShortcutRow, PreviousTabShortcutRow, TheatreShortcutRow
        }.ToDictionary(row => row.Action);
        foreach (var (action, row) in _rows)
        {
            row.CaptureRequested += (_, _) => BeginCapturingShortcut(action);
            row.SetKeysText(ShortcutText.Format(_shortcuts[action]));
            UpdateShortcutStatus(action);
        }

        ShowPage(DisplayPage);
    }

    public bool FillGameToPanel { get; private set; }

    public bool ShowFullScreenExitButton { get; private set; }

    public bool ShowOverlaysInTheatreMode { get; private set; }

    public SecondMonitorMode SecondMonitorMode { get; private set; }

    public bool BlockStorePages { get; private set; }

    public IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> Shortcuts => _shortcuts;

    public bool ResetLayoutSizes { get; private set; }

    private IEnumerable<(Button Tab, ScrollViewer Page)> Tabs() =>
    [
        (DisplayTabButton, DisplayPage),
        (LayoutTabButton, LayoutPage),
        (ShortcutsTabButton, ShortcutsPage)
    ];

    private void TabButton_Click(object sender, RoutedEventArgs e)
    {
        // A shortcut being captured belongs to the page being left.
        CancelCapture();
        ShowPage(Tabs().First(tab => ReferenceEquals(tab.Tab, sender)).Page);
    }

    private void ShowPage(ScrollViewer page)
    {
        foreach (var (tab, candidate) in Tabs())
        {
            var active = ReferenceEquals(candidate, page);
            candidate.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            tab.Opacity = active ? 1d : 0.65d;
        }
    }

    internal void BeginCapturingShortcut(GlobalShortcutAction action)
    {
        _capturingShortcut = action;
        _rows[action].SetKeysText("Press shortcut…");
        _rows[action].SetStatus(ShortcutCapture.Prompt);
        Activate();
        Keyboard.Focus(this);
    }

    internal string? DuplicateShortcutMessage() =>
        ShortcutCapture.DuplicateMessage(_shortcuts);

    private void ResetLayoutSizes_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
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

    private void SettingsDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingShortcut is not { } action)
        {
            return;
        }

        e.Handled = true;
        switch (ShortcutCapture.Read(e, out var chord))
        {
            case ShortcutKeyResult.Cancelled:
                CancelCapture();
                break;
            case ShortcutKeyResult.Invalid:
                _rows[action].SetStatus(ShortcutCapture.InvalidKeysMessage);
                break;
            case ShortcutKeyResult.Captured:
                Capture(action, chord!);
                break;
        }
    }

    private void SettingsDialog_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_capturingShortcut is { } action && ShortcutCapture.TryReadMouseButton(e, out var chord))
        {
            e.Handled = true;
            Capture(action, chord!);
        }
    }

    private void SettingsDialog_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_capturingShortcut is { } action && ShortcutCapture.TryReadWheel(e, out var chord))
        {
            e.Handled = true;
            Capture(action, chord!);
        }
    }

    private void Capture(GlobalShortcutAction action, GlobalHotkeyChord chord)
    {
        _shortcuts[action] = chord;
        _capturingShortcut = null;
        _rows[action].SetKeysText(ShortcutText.Format(chord));
        UpdateShortcutStatus(action);
    }

    private void CancelCapture()
    {
        if (_capturingShortcut is not { } action)
        {
            return;
        }

        _capturingShortcut = null;
        _rows[action].SetKeysText(ShortcutText.Format(_shortcuts[action]));
        UpdateShortcutStatus(action);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        if (DuplicateShortcutMessage() is { } message)
        {
            ShowPage(ShortcutsPage);
            MessageBox.Show(this, message, "Shortcuts must be different", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FillGameToPanel = FillOption.IsChecked == true;
        ShowFullScreenExitButton = ShowFullScreenExitOption.IsChecked == true;
        ShowOverlaysInTheatreMode = ShowOverlaysInTheatreModeOption.IsChecked == true;
        SecondMonitorMode = FloatingCardsModeOption.IsChecked == true
            ? SecondMonitorMode.FloatingCards
            : SecondMonitorMode.AccountToolsWindow;
        BlockStorePages = BlockStorePagesOption.IsChecked == true;
        DialogResult = true;
    }

    private void UpdateShortcutStatus(GlobalShortcutAction action)
    {
        _rows[action].SetStatus(_shortcuts[action] != _initialShortcuts[action]
            ? "Save settings to register this shortcut."
            : _unavailableShortcuts.Contains(action)
                ? "Unavailable — another app or another FourFold shortcut may be using these keys. Choose a different combination."
                : string.Empty);
    }

}
