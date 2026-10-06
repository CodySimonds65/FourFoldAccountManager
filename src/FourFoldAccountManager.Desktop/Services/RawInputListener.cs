using System.Runtime.InteropServices;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Services;

// Hears every key press, and while a mouse shortcut is bound the mouse buttons and wheel, through Windows raw
// input without taking them from the game or any other app. Observed shortcuts use this; key chords with a
// modifier still go through RegisterHotKey. WM_INPUT must be left unhandled so DefWindowProc can clean up
// after it.
internal sealed class RawInputListener : IDisposable
{
    private const ushort GenericDesktopPage = 0x01;
    private const ushort MouseUsage = 0x02;
    private const ushort KeyboardUsage = 0x06;
    private const uint RemoveDevice = 0x00000001;
    private const uint InputSink = 0x00000100;
    private const uint RawInputCommand = 0x10000003;
    private const uint MouseInput = 0;
    private const uint KeyboardInput = 1;
    private const ushort KeyBreak = 0x01;
    private const ushort KeyE0 = 0x02;
    private const int NumLock = 0x90;
    private const ushort WheelFlag = 0x0400;
    private const int WheelNotch = 120;

    // RAWMOUSE button flags: pressed, released, and the shortcut input they stand for.
    private static readonly (ushort Down, ushort Up, ushort Input)[] MouseButtons =
    [
        (0x0010, 0x0020, GlobalHotkeyChord.MiddleClick),
        (0x0040, 0x0080, GlobalHotkeyChord.MouseButton4),
        (0x0100, 0x0200, GlobalHotkeyChord.MouseButton5)
    ];

    private readonly IntPtr _windowHandle;
    private readonly Action<ushort, bool> _onInput;
    private bool _mouseEnabled;
    private int _wheelRemainder;

    private RawInputListener(IntPtr windowHandle, Action<ushort, bool> onInput)
    {
        _windowHandle = windowHandle;
        _onInput = onInput;
    }

    // Returns null when Windows refuses the registration; observed shortcuts then show as unavailable.
    // onInput receives each key or mouse input and whether it was pressed (true) or released (false).
    public static RawInputListener? TryRegister(IntPtr windowHandle, Action<ushort, bool> onInput) =>
        Register(KeyboardUsage, InputSink, windowHandle) ? new RawInputListener(windowHandle, onInput) : null;

    // Once the mouse is registered every mouse movement arrives as a message too, so it is only switched on
    // while a mouse shortcut is bound.
    public void SetMouseEnabled(bool enabled)
    {
        if (enabled != _mouseEnabled &&
            Register(MouseUsage, enabled ? InputSink : RemoveDevice, enabled ? _windowHandle : IntPtr.Zero))
        {
            _mouseEnabled = enabled;
            _wheelRemainder = 0;
        }
    }

    // Raw input reaches this window whichever app is in front; mouse shortcuts only act while FourFold is.
    public static bool IsThisAppInFront() =>
        GetWindowThreadProcessId(GetForegroundWindow(), out var processId) != 0 && processId == Environment.ProcessId;

    // Reads one WM_INPUT event and reports what it holds. Other devices and the fake keys Windows inserts
    // report nothing.
    public void Read(IntPtr rawInputHandle)
    {
        var headerSize = (uint)(8 + 2 * IntPtr.Size);
        uint size = 0;
        if (GetRawInputData(rawInputHandle, RawInputCommand, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInputHandle, RawInputCommand, buffer, ref size, headerSize) != size)
            {
                return;
            }

            var data = buffer + (int)headerSize;
            switch ((uint)Marshal.ReadInt32(buffer))
            {
                case KeyboardInput:
                    ReadKeyboard(data);
                    break;
                case MouseInput:
                    // RAWMOUSE: Flags (16-bit), padding, then ButtonFlags and ButtonData (both 16-bit).
                    DecodeMouse(
                        (ushort)Marshal.ReadInt16(data, 4), Marshal.ReadInt16(data, 6), ref _wheelRemainder, _onInput);
                    break;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // Turns one raw mouse event into shortcut inputs. Left click, right click, sideways scrolling and plain
    // movement report nothing. The wheel has no release, so each notch is a press followed at once by its
    // release; smooth-scrolling wheels send fractions of a notch, which add up in wheelRemainder first.
    internal static void DecodeMouse(
        ushort buttonFlags, short wheelDelta, ref int wheelRemainder, Action<ushort, bool> onInput)
    {
        foreach (var (down, up, input) in MouseButtons)
        {
            if ((buttonFlags & down) != 0)
            {
                onInput(input, true);
            }

            if ((buttonFlags & up) != 0)
            {
                onInput(input, false);
            }
        }

        if ((buttonFlags & WheelFlag) == 0 || wheelDelta == 0)
        {
            return;
        }

        // Turning the wheel the other way starts a fresh notch.
        wheelRemainder = Math.Sign(wheelRemainder) == -Math.Sign(wheelDelta) ? wheelDelta : wheelRemainder + wheelDelta;
        var scroll = wheelRemainder > 0 ? GlobalHotkeyChord.ScrollUp : GlobalHotkeyChord.ScrollDown;
        for (; Math.Abs(wheelRemainder) >= WheelNotch; wheelRemainder -= Math.Sign(wheelRemainder) * WheelNotch)
        {
            onInput(scroll, true);
            onInput(scroll, false);
        }
    }

    public void Dispose()
    {
        _ = Register(KeyboardUsage, RemoveDevice, IntPtr.Zero);
        SetMouseEnabled(false);
    }

    private void ReadKeyboard(IntPtr keyboard)
    {
        // RAWKEYBOARD: MakeCode, Flags, Reserved, VKey (all 16-bit), right after the header.
        var makeCode = (ushort)Marshal.ReadInt16(keyboard, 0);
        var flags = (ushort)Marshal.ReadInt16(keyboard, 2);
        var reportedKey = (ushort)Marshal.ReadInt16(keyboard, 6);
        var isE0 = (flags & KeyE0) != 0;
        // 0xFF marks keys Windows fakes; some keyboards also wrap numpad keys in an extra Shift carrying the
        // E0 flag, which a real Shift press never has.
        if (reportedKey is 0 or 0xFF || (reportedKey == 0x10 && isE0))
        {
            return;
        }

        _onInput(Normalize(reportedKey, makeCode, isE0), (flags & KeyBreak) == 0);
    }

    private static bool Register(ushort usage, uint flags, IntPtr target)
    {
        var device = new RawInputDevice
        {
            UsagePage = GenericDesktopPage,
            Usage = usage,
            Flags = flags,
            Target = target
        };
        return RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    // Raw input reports generic Shift, Ctrl, and Alt, and numpad keys as the navigation keys they share, so map
    // them to what a key press in a window reports; recorded shortcuts use those codes.
    private static ushort Normalize(ushort reportedKey, ushort makeCode, bool isE0) => reportedKey switch
    {
        0x10 => makeCode == 0x36 ? (ushort)0xA1 : (ushort)0xA0,
        0x11 => isE0 ? (ushort)0xA3 : (ushort)0xA2,
        0x12 => isE0 ? (ushort)0xA5 : (ushort)0xA4,
        _ when !isE0 && (GetKeyState(NumLock) & 1) != 0 && NumpadKey(reportedKey) is { } numpad => numpad,
        _ => reportedKey
    };

    private static ushort? NumpadKey(ushort navigationKey) => navigationKey switch
    {
        0x2D => 0x60, // Insert -> Num 0
        0x23 => 0x61, // End -> Num 1
        0x28 => 0x62, // Down -> Num 2
        0x22 => 0x63, // Page Down -> Num 3
        0x25 => 0x64, // Left -> Num 4
        0x0C => 0x65, // Clear -> Num 5
        0x27 => 0x66, // Right -> Num 6
        0x24 => 0x67, // Home -> Num 7
        0x26 => 0x68, // Up -> Num 8
        0x21 => 0x69, // Page Up -> Num 9
        0x2E => 0x6E, // Delete -> Num .
        _ => null
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
}
