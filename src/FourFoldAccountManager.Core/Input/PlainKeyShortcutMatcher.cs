using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Input;

// Decides which observed shortcut a key press or mouse input fires. These are only observed, never blocked,
// so this tracks held keys itself: a held key fires once, and held Ctrl, Alt, or Shift never stop a shortcut
// bound without them unless that exact combination is another active shortcut (a key chord with a modifier
// fires through Windows instead; a mouse shortcut with modifiers fires here, on exactly those modifiers).
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
        GlobalShortcutAction? unmodified = null;
        foreach (var (action, chord) in _bindings)
        {
            if (chord == pressed)
            {
                // A key chord with a modifier fires through Windows, so it is not fired again here.
                return chord.IsObserved ? action : null;
            }

            if (chord.VirtualKey == virtualKey && chord.Modifiers == GlobalHotkeyModifiers.None)
            {
                unmodified = action;
            }
        }

        return unmodified;
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
