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

        return RegisterHotKey(_windowHandle, id, modifiers, chord.VirtualKey);
    }

    public void Unregister(int id) => _ = UnregisterHotKey(_windowHandle, id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
