using System.Windows.Input;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

internal enum ShortcutKeyResult
{
    Waiting,
    Cancelled,
    Invalid,
    Captured
}

// Key-capture rules shared by Settings → Shortcuts and plugin settings pages.
internal static class ShortcutCapture
{
    public const string Prompt = "Press a key, with or without Ctrl, Alt, or Shift. Esc cancels.";

    public const string InvalidKeysMessage =
        "Esc, Tab, Enter, Backspace, F5, the Windows key, and Ctrl, Alt, or Shift on their own can't be shortcuts.";

    public static ShortcutKeyResult Read(KeyEventArgs e, out GlobalHotkeyChord? chord)
    {
        chord = null;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };
        if (key == Key.Escape)
        {
            return ShortcutKeyResult.Cancelled;
        }

        // Pressing Ctrl, Alt, or Shift first is how a chord starts; keep waiting for its main key.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
        {
            return ShortcutKeyResult.Waiting;
        }

        // Without this, Win+F would be recorded as a plain F.
        if (key is Key.LWin or Key.RWin || (Keyboard.Modifiers & ModifierKeys.Windows) != 0)
        {
            return ShortcutKeyResult.Invalid;
        }

        if (!GlobalHotkeyChord.TryCreate(
                (ushort)KeyInterop.VirtualKeyFromKey(key), MapSupportedModifiers(Keyboard.Modifiers), out var created))
        {
            return ShortcutKeyResult.Invalid;
        }

        chord = created;
        return ShortcutKeyResult.Captured;
    }

    public static string? DuplicateMessage(IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> chords) =>
        GlobalShortcutActions.FindDuplicate(chords) is { } duplicate
            ? $"{GlobalShortcutActions.DisplayName(duplicate.First)} and " +
              $"{GlobalShortcutActions.DisplayName(duplicate.Second)} use the same keys. " +
              "Choose a different shortcut for one of them."
            : null;

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
