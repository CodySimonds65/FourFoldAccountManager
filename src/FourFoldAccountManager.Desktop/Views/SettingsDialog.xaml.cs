using System.Windows;
using System.Windows.Input;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

public partial class SettingsDialog : Window
{
    private const string CapturePrompt =
        "Press Ctrl, Alt, or Shift with one key, or a single numpad, F13–F24, Pause, Scroll Lock, or Insert key. Esc cancels.";

    private const string InvalidKeysMessage =
        "Use Ctrl, Alt, or Shift plus one key, or a single numpad (with Num Lock on), F13–F24, Pause, Scroll Lock, or Insert key. Windows-key shortcuts are not supported.";

    private readonly Func<MessageBoxResult>? _confirmResetLayoutSizes;
    private readonly IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> _initialShortcuts;
    private readonly IReadOnlySet<GlobalShortcutAction> _unavailableShortcuts;
    private readonly Dictionary<GlobalShortcutAction, GlobalHotkeyChord> _shortcuts;
    private readonly IReadOnlyDictionary<GlobalShortcutAction, ShortcutRow> _rows;
    private GlobalShortcutAction? _capturingShortcut;

    public SettingsDialog(bool fillGameToPanel, bool showFullScreenExitButton)
        : this(fillGameToPanel, showFullScreenExitButton, DefaultShortcuts(), new HashSet<GlobalShortcutAction>())
    {
    }

    internal SettingsDialog(
        bool fillGameToPanel,
        bool showFullScreenExitButton,
        Func<MessageBoxResult>? confirmResetLayoutSizes)
        : this(fillGameToPanel, showFullScreenExitButton, DefaultShortcuts(), new HashSet<GlobalShortcutAction>(),
            confirmResetLayoutSizes)
    {
    }

    internal SettingsDialog(
        bool fillGameToPanel,
        bool showFullScreenExitButton,
        IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> shortcuts,
        IReadOnlySet<GlobalShortcutAction> unavailableShortcuts,
        Func<MessageBoxResult>? confirmResetLayoutSizes = null)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(unavailableShortcuts);
        if (GlobalShortcutActions.All.Any(action => !shortcuts.ContainsKey(action)))
        {
            throw new ArgumentException("Every shortcut action needs a chord.", nameof(shortcuts));
        }

        _confirmResetLayoutSizes = confirmResetLayoutSizes;
        _initialShortcuts = new Dictionary<GlobalShortcutAction, GlobalHotkeyChord>(shortcuts);
        _shortcuts = new Dictionary<GlobalShortcutAction, GlobalHotkeyChord>(shortcuts);
        _unavailableShortcuts = unavailableShortcuts;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        FillOption.IsChecked = fillGameToPanel;
        FitOption.IsChecked = !fillGameToPanel;
        ShowFullScreenExitOption.IsChecked = showFullScreenExitButton;
        _rows = new[]
        {
            RevealShortcutRow, DividerShortcutRow, TimerSplitShortcutRow, TimerFinishShortcutRow, TimerResetShortcutRow
        }.ToDictionary(row => row.Action);
        foreach (var (action, row) in _rows)
        {
            row.CaptureRequested += (_, _) => BeginCapturingShortcut(action);
            row.SetKeysText(ShortcutText.Format(_shortcuts[action]));
            UpdateShortcutStatus(action);
        }
    }

    public bool FillGameToPanel { get; private set; }

    public bool ShowFullScreenExitButton { get; private set; }

    public IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> Shortcuts => _shortcuts;

    public bool ResetLayoutSizes { get; private set; }

    internal ShortcutRow RowFor(GlobalShortcutAction action) => _rows[action];

    internal void BeginCapturingShortcut(GlobalShortcutAction action)
    {
        _capturingShortcut = action;
        _rows[action].SetKeysText("Press shortcut…");
        _rows[action].SetStatus(CapturePrompt);
        Activate();
        Keyboard.Focus(this);
    }

    // Returns false, and explains why in the row, when the keys cannot be a global shortcut.
    internal bool TryApplyCapturedKey(ushort virtualKey, GlobalHotkeyModifiers modifiers)
    {
        if (_capturingShortcut is not { } action)
        {
            return false;
        }

        if (!GlobalHotkeyChord.TryCreate(virtualKey, modifiers, out var chord))
        {
            _rows[action].SetStatus(InvalidKeysMessage);
            return false;
        }

        _shortcuts[action] = chord;
        _capturingShortcut = null;
        _rows[action].SetKeysText(ShortcutText.Format(chord));
        UpdateShortcutStatus(action);
        return true;
    }

    internal string? DuplicateShortcutMessage()
    {
        if (GlobalShortcutActions.FindDuplicate(_shortcuts) is not { } duplicate)
        {
            return null;
        }

        return $"{GlobalShortcutActions.DisplayName(duplicate.First)} and " +
            $"{GlobalShortcutActions.DisplayName(duplicate.Second)} use the same keys. " +
            "Choose a different shortcut for one of them.";
    }

    private static IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> DefaultShortcuts() =>
        GlobalShortcutActions.All.ToDictionary(
            action => action, action => GlobalShortcutActions.GetChord(PanelSettings.Default, action));

    private void ResetLayoutSizes_Click(object sender, RoutedEventArgs e)
    {
        var result = _confirmResetLayoutSizes?.Invoke() ?? MessageBox.Show(
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
        if (_capturingShortcut is null)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CancelCapture();
            return;
        }

        TryApplyCapturedKey((ushort)KeyInterop.VirtualKeyFromKey(key), MapSupportedModifiers(Keyboard.Modifiers));
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
            MessageBox.Show(this, message, "Shortcuts must be different", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FillGameToPanel = FillOption.IsChecked == true;
        ShowFullScreenExitButton = ShowFullScreenExitOption.IsChecked == true;
        DialogResult = true;
    }

    private void UpdateShortcutStatus(GlobalShortcutAction action)
    {
        _rows[action].SetStatus(_shortcuts[action] != _initialShortcuts[action]
            ? "Save settings to register this shortcut."
            : _unavailableShortcuts.Contains(action)
                ? "Unavailable — another app or another FourFold shortcut may be using these keys. Choose a different combination."
                : "Available globally, including while a game window is focused.");
    }

    private static GlobalHotkeyModifiers MapSupportedModifiers(ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            return GlobalHotkeyModifiers.None;
        }

        var result = GlobalHotkeyModifiers.None;
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            result |= GlobalHotkeyModifiers.Control;
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            result |= GlobalHotkeyModifiers.Alt;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            result |= GlobalHotkeyModifiers.Shift;
        }

        return result;
    }
}
