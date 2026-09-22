using System.Windows;
using System.Windows.Input;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

public partial class SettingsDialog : Window
{
    private readonly Func<MessageBoxResult>? _confirmResetLayoutSizes;
    private readonly GlobalHotkeyChord _initialRevealXpOverlayTabShortcut;
    private readonly bool _revealShortcutAvailable;
    private bool _isCapturingRevealShortcut;

    public SettingsDialog(bool fillGameToPanel, bool showFullScreenExitButton) :
        this(
            fillGameToPanel,
            showFullScreenExitButton,
            GlobalHotkeyChord.DefaultRevealXpOverlayTab,
            revealShortcutAvailable: true,
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
            confirmResetLayoutSizes: confirmResetLayoutSizes)
    {
    }

    internal SettingsDialog(
        bool fillGameToPanel,
        bool showFullScreenExitButton,
        GlobalHotkeyChord revealXpOverlayTabShortcut,
        bool revealShortcutAvailable,
        Func<MessageBoxResult>? confirmResetLayoutSizes = null)
    {
        ArgumentNullException.ThrowIfNull(revealXpOverlayTabShortcut);
        _confirmResetLayoutSizes = confirmResetLayoutSizes;
        _initialRevealXpOverlayTabShortcut = revealXpOverlayTabShortcut;
        _revealShortcutAvailable = revealShortcutAvailable;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        FillOption.IsChecked = fillGameToPanel;
        FitOption.IsChecked = !fillGameToPanel;
        ShowFullScreenExitOption.IsChecked = showFullScreenExitButton;
        RevealXpOverlayTabShortcut = revealXpOverlayTabShortcut;
        RevealShortcutButton.Content = FormatShortcut(revealXpOverlayTabShortcut);
        UpdateRevealShortcutStatus();
    }

    public bool FillGameToPanel { get; private set; }

    public bool ShowFullScreenExitButton { get; private set; }

    public GlobalHotkeyChord RevealXpOverlayTabShortcut { get; private set; } =
        GlobalHotkeyChord.DefaultRevealXpOverlayTab;

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

    private void CaptureRevealShortcut_Click(object sender, RoutedEventArgs e)
    {
        _isCapturingRevealShortcut = true;
        RevealShortcutButton.Content = "Press shortcut…";
        RevealShortcutStatusText.Text = "Press Ctrl, Alt, or Shift with one key. Esc cancels.";
        Activate();
        Keyboard.Focus(this);
    }

    private void SettingsDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isCapturingRevealShortcut)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _isCapturingRevealShortcut = false;
            RevealShortcutButton.Content = FormatShortcut(RevealXpOverlayTabShortcut);
            UpdateRevealShortcutStatus();
            return;
        }

        var virtualKey = (ushort)KeyInterop.VirtualKeyFromKey(key);
        var modifiers = MapSupportedModifiers(Keyboard.Modifiers);
        if (!GlobalHotkeyChord.TryCreate(virtualKey, modifiers, out var chord))
        {
            RevealShortcutStatusText.Text =
                "Use Ctrl, Alt, or Shift plus one non-modifier key. Windows-key shortcuts are not supported.";
            return;
        }

        RevealXpOverlayTabShortcut = chord;
        _isCapturingRevealShortcut = false;
        RevealShortcutButton.Content = FormatShortcut(chord);
        UpdateRevealShortcutStatus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _isCapturingRevealShortcut = false;
        FillGameToPanel = FillOption.IsChecked == true;
        ShowFullScreenExitButton = ShowFullScreenExitOption.IsChecked == true;
        DialogResult = true;
    }

    private void UpdateRevealShortcutStatus()
    {
        if (RevealXpOverlayTabShortcut != _initialRevealXpOverlayTabShortcut)
        {
            RevealShortcutStatusText.Text = "Save settings to register this shortcut.";
        }
        else
        {
            RevealShortcutStatusText.Text = _revealShortcutAvailable
                ? "Available globally, including while a game window is focused."
                : "Unavailable — another app may be using this shortcut. Choose a different combination.";
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
}
