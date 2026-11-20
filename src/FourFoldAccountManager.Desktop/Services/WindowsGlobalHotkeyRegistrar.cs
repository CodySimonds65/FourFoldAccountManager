using System.Runtime.InteropServices;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Services;

internal sealed class WindowsGlobalHotkeyRegistrar : IGlobalHotkeyRegistrar
{
    private const uint ModifierAlt = 0x0001;
    private const uint ModifierControl = 0x0002;
    private const uint ModifierShift = 0x0004;
    private const uint ModifierNoRepeat = 0x4000;

    private readonly IntPtr _windowHandle;
    private readonly Dictionary<int, (uint Modifiers, uint Key)> _registered = [];
    private bool _suspended;

    public WindowsGlobalHotkeyRegistrar(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A window handle is required to register a global shortcut.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
    }

    public bool TryRegister(int id, GlobalHotkeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (!chord.IsValid)
        {
            return false;
        }

        var modifiers = ModifierNoRepeat;
        if (chord.Modifiers.HasFlag(GlobalHotkeyModifiers.Control))
        {
            modifiers |= ModifierControl;
        }

        if (chord.Modifiers.HasFlag(GlobalHotkeyModifiers.Alt))
        {
            modifiers |= ModifierAlt;
        }

        if (chord.Modifiers.HasFlag(GlobalHotkeyModifiers.Shift))
        {
            modifiers |= ModifierShift;
        }

        if (!RegisterHotKey(_windowHandle, id, modifiers, chord.VirtualKey))
        {
            return false;
        }

        // While suspended the trial registration only proves Windows would accept the chord.
        if (_suspended)
        {
            _ = UnregisterHotKey(_windowHandle, id);
        }

        _registered[id] = (modifiers, chord.VirtualKey);
        return true;
    }

    public void Unregister(int id)
    {
        _registered.Remove(id);
        _ = UnregisterHotKey(_windowHandle, id);
    }

    // RegisterHotKey takes its chord from every app, so the chords are only held while FourFold is in front.
    public void SetSuspended(bool suspended)
    {
        if (suspended == _suspended)
        {
            return;
        }

        _suspended = suspended;
        foreach (var (id, chord) in _registered)
        {
            _ = suspended
                ? UnregisterHotKey(_windowHandle, id)
                : RegisterHotKey(_windowHandle, id, chord.Modifiers, chord.Key);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
