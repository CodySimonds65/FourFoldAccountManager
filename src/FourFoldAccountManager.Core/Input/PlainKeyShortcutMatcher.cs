using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Input;

// Decides which plain-key shortcut a key press fires. Plain keys are only observed, never blocked, so this
// tracks held keys itself: a held key fires once, and held Ctrl, Alt, or Shift never stop a plain key unless
// that exact combination is another active shortcut's modifier chord (which fires through Windows instead).
public sealed class PlainKeyShortcutMatcher
{
    private readonly HashSet<ushort> _held = [];
    private Dictionary<GlobalShortcutAction, GlobalHotkeyChord> _bindings = [];

    // The chords of the actions that are live right now, plain keys and modifier chords alike.
    public void SetBindings(IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = new Dictionary<GlobalShortcutAction, GlobalHotkeyChord>(bindings);
    }

    public GlobalShortcutAction? KeyDown(ushort virtualKey)
    {
        if (!_held.Add(virtualKey))
        {
            return null; // A repeat while the key is held.
        }

        var pressed = new GlobalHotkeyChord(virtualKey, HeldModifiers());
        if (!pressed.IsPlainKey && _bindings.ContainsValue(pressed))
        {
            return null;
        }

        foreach (var (action, chord) in _bindings)
        {
            if (chord.IsPlainKey && chord.VirtualKey == virtualKey)
            {
                return action;
            }
        }

        return null;
    }

    public void KeyUp(ushort virtualKey) => _held.Remove(virtualKey);

    private GlobalHotkeyModifiers HeldModifiers()
    {
        var modifiers = GlobalHotkeyModifiers.None;
        if (_held.Contains(0x10) || _held.Contains(0xA0) || _held.Contains(0xA1))
        {
            modifiers |= GlobalHotkeyModifiers.Shift;
        }

        if (_held.Contains(0x11) || _held.Contains(0xA2) || _held.Contains(0xA3))
        {
            modifiers |= GlobalHotkeyModifiers.Control;
        }

        if (_held.Contains(0x12) || _held.Contains(0xA4) || _held.Contains(0xA5))
        {
            modifiers |= GlobalHotkeyModifiers.Alt;
        }

        return modifiers;
    }
}
