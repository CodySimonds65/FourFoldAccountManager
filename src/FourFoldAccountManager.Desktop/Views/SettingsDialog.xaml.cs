using System.Windows;
using System.Windows.Input;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

public partial class SettingsDialog : Window
{
    private readonly Func<MessageBoxResult>? _confirmResetLayoutSizes;
    private readonly GlobalHotkeyChord _initialRevealXpOverlayTabShortcut;
    private readonly GlobalHotkeyChord _initialToggleDividerResizingShortcut;
    private readonly bool _revealShortcutAvailable;
    private readonly bool _toggleDividerShortcutAvailable;
    private ShortcutTarget? _capturingShortcut;

    public SettingsDialog(bool fillGameToPanel, bool showFullScreenExitButton) :
        this(
            fillGameToPanel,
            showFullScreenExitButton,
            GlobalHotkeyChord.DefaultRevealXpOverlayTab,
            revealShortcutAvailable: true,
            GlobalHotkeyChord.DefaultToggleDividerResizing,
            toggleDividerShortcutAvailable: true,
            confirmResetLayoutSizes: null)
    {
    }

    internal SettingsDialog(
        bool fillGameToPanel,
        bool showFullScreenExitButton,
        Func<MessageBoxResult>? confirmResetLayoutSizes)
        : this(
            fillGameToPanel,
            showFullScreenExitButton,
            GlobalHotkeyChord.DefaultRevealXpOverlayTab,
            revealShortcutAvailable: true,
            GlobalHotkeyChord.DefaultToggleDividerResizing,
            toggleDividerShortcutAvailable: true,
            confirmResetLayoutSizes)
    {
    }

    internal SettingsDialog(
        bool fillGameToPanel,
        bool showFullScreenExitButton,
        GlobalHotkeyChord revealXpOverlayTabShortcut,
        bool revealShortcutAvailable,
        Func<MessageBoxResult>? confirmResetLayoutSizes = null)
        : this(
            fillGameToPanel,
            showFullScreenExitButton,
            revealXpOverlayTabShortcut,
            revealShortcutAvailable,
            GlobalHotkeyChord.DefaultToggleDividerResizing,
            toggleDividerShortcutAvailable: true,
            confirmResetLayoutSizes)
    {
    }

    internal SettingsDialog(
        bool fillGameToPanel,
        bool showFullScreenExitButton,
        GlobalHotkeyChord revealXpOverlayTabShortcut,
        bool revealShortcutAvailable,
        GlobalHotkeyChord toggleDividerResizingShortcut,
        bool toggleDividerShortcutAvailable,
        Func<MessageBoxResult>? confirmResetLayoutSizes = null)
    {
        ArgumentNullException.ThrowIfNull(revealXpOverlayTabShortcut);
        ArgumentNullException.ThrowIfNull(toggleDividerResizingShortcut);
        _confirmResetLayoutSizes = confirmResetLayoutSizes;
        _initialRevealXpOverlayTabShortcut = revealXpOverlayTabShortcut;
        _initialToggleDividerResizingShortcut = toggleDividerResizingShortcut;
        _revealShortcutAvailable = revealShortcutAvailable;
        _toggleDividerShortcutAvailable = toggleDividerShortcutAvailable;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        FillOption.IsChecked = fillGameToPanel;
        FitOption.IsChecked = !fillGameToPanel;
        ShowFullScreenExitOption.IsChecked = showFullScreenExitButton;
        RevealXpOverlayTabShortcut = revealXpOverlayTabShortcut;
        ToggleDividerResizingShortcut = toggleDividerResizingShortcut;
        RevealShortcutButton.Content = FormatShortcut(revealXpOverlayTabShortcut);
        ToggleDividerResizeShortcutButton.Content = FormatShortcut(toggleDividerResizingShortcut);
        UpdateShortcutStatus(ShortcutTarget.RevealXpOverlayTab);
        UpdateShortcutStatus(ShortcutTarget.ToggleDividerResizing);
    }

    public bool FillGameToPanel { get; private set; }

    public bool ShowFullScreenExitButton { get; private set; }

    public GlobalHotkeyChord RevealXpOverlayTabShortcut { get; private set; } =
        GlobalHotkeyChord.DefaultRevealXpOverlayTab;

    public GlobalHotkeyChord ToggleDividerResizingShortcut { get; private set; } =
        GlobalHotkeyChord.DefaultToggleDividerResizing;

    public bool ResetLayoutSizes { get; private set; }

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

    private void CaptureRevealShortcut_Click(object sender, RoutedEventArgs e) =>
        BeginCapturingShortcut(ShortcutTarget.RevealXpOverlayTab);

    private void CaptureToggleDividerResizeShortcut_Click(object sender, RoutedEventArgs e) =>
        BeginCapturingShortcut(ShortcutTarget.ToggleDividerResizing);

    private void BeginCapturingShortcut(ShortcutTarget target)
    {
        _capturingShortcut = target;
        SetShortcutButtonContent(target, "Press shortcut…");
        SetShortcutStatus(target, "Press Ctrl, Alt, or Shift with one key. Esc cancels.");
        Activate();
        Keyboard.Focus(this);
    }

    private void SettingsDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingShortcut is not { } target)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _capturingShortcut = null;
            SetShortcutButtonContent(target, FormatShortcut(GetShortcut(target)));
            UpdateShortcutStatus(target);
            return;
        }

        var virtualKey = (ushort)KeyInterop.VirtualKeyFromKey(key);
        var modifiers = MapSupportedModifiers(Keyboard.Modifiers);
        if (!GlobalHotkeyChord.TryCreate(virtualKey, modifiers, out var chord))
        {
            SetShortcutStatus(target,
                "Use Ctrl, Alt, or Shift plus one non-modifier key. Windows-key shortcuts are not supported.");
            return;
        }

        SetShortcut(target, chord);
        _capturingShortcut = null;
        SetShortcutButtonContent(target, FormatShortcut(chord));
        UpdateShortcutStatus(target);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _capturingShortcut = null;
        if (RevealXpOverlayTabShortcut == ToggleDividerResizingShortcut)
        {
            MessageBox.Show(this,
                "Choose different shortcuts for revealing the XP overlay tab and toggling divider resizing.",
                "Shortcuts must be different", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FillGameToPanel = FillOption.IsChecked == true;
        ShowFullScreenExitButton = ShowFullScreenExitOption.IsChecked == true;
        DialogResult = true;
    }

    private void UpdateShortcutStatus(ShortcutTarget target)
    {
        var chord = GetShortcut(target);
        var initialChord = target == ShortcutTarget.RevealXpOverlayTab
            ? _initialRevealXpOverlayTabShortcut
            : _initialToggleDividerResizingShortcut;
        var available = target == ShortcutTarget.RevealXpOverlayTab
            ? _revealShortcutAvailable
            : _toggleDividerShortcutAvailable;

        SetShortcutStatus(target, chord != initialChord
            ? "Save settings to register this shortcut."
            : available
                ? "Available globally, including while a game window is focused."
                : "Unavailable — another app may be using this shortcut. Choose a different combination.");
    }

    private GlobalHotkeyChord GetShortcut(ShortcutTarget target) =>
        target == ShortcutTarget.RevealXpOverlayTab
            ? RevealXpOverlayTabShortcut
            : ToggleDividerResizingShortcut;

    private void SetShortcut(ShortcutTarget target, GlobalHotkeyChord chord)
    {
        if (target == ShortcutTarget.RevealXpOverlayTab)
        {
            RevealXpOverlayTabShortcut = chord;
        }
        else
        {
            ToggleDividerResizingShortcut = chord;
        }
    }

    private void SetShortcutButtonContent(ShortcutTarget target, string content)
    {
        if (target == ShortcutTarget.RevealXpOverlayTab)
        {
            RevealShortcutButton.Content = content;
        }
        else
        {
            ToggleDividerResizeShortcutButton.Content = content;
        }
    }

    private void SetShortcutStatus(ShortcutTarget target, string status)
    {
        if (target == ShortcutTarget.RevealXpOverlayTab)
        {
            RevealShortcutStatusText.Text = status;
        }
        else
        {
            ToggleDividerResizeShortcutStatusText.Text = status;
        }
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

    private static string FormatShortcut(GlobalHotkeyChord chord)
    {
        var parts = new List<string>();
        if (chord.Modifiers.HasFlag(GlobalHotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (chord.Modifiers.HasFlag(GlobalHotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (chord.Modifiers.HasFlag(GlobalHotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        var keyName = KeyInterop.KeyFromVirtualKey(chord.VirtualKey).ToString();
        if (keyName.Length == 2 && keyName[0] == 'D' && char.IsDigit(keyName[1]))
        {
            keyName = keyName[1].ToString();
        }

        parts.Add(keyName);
        return string.Join("+", parts);
    }

    private enum ShortcutTarget
    {
        RevealXpOverlayTab,
        ToggleDividerResizing
    }
}
