using System.Runtime.InteropServices;

namespace FourFoldAccountManager.Desktop.Services;

// Hears every key press through Windows raw input without taking it from the game or any other app. Plain-key
// shortcuts use this; modifier chords still go through RegisterHotKey. WM_INPUT must be left unhandled so
// DefWindowProc can clean up after it.
internal sealed class RawKeyboardListener : IDisposable
{
    private const ushort GenericDesktopPage = 0x01;
    private const ushort KeyboardUsage = 0x06;
    private const uint RemoveDevice = 0x00000001;
    private const uint InputSink = 0x00000100;
    private const uint RawInputCommand = 0x10000003;
    private const uint KeyboardInput = 1;
    private const ushort KeyBreak = 0x01;
    private const ushort KeyE0 = 0x02;
    private const int NumLock = 0x90;

    private RawKeyboardListener()
    {
    }

    // Returns null when Windows refuses the registration; plain-key shortcuts then show as unavailable.
    public static RawKeyboardListener? TryRegister(IntPtr windowHandle)
    {
        var device = new RawInputDevice
        {
            UsagePage = GenericDesktopPage,
            Usage = KeyboardUsage,
            Flags = InputSink,
            Target = windowHandle
        };
        return RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>())
            ? new RawKeyboardListener()
            : null;
    }

    // Reads one WM_INPUT event. Returns false for other devices and for the fake keys Windows inserts.
    public bool TryRead(IntPtr rawInputHandle, out ushort virtualKey, out bool isKeyDown)
    {
        virtualKey = 0;
        isKeyDown = false;
        var headerSize = (uint)(8 + 2 * IntPtr.Size);
        uint size = 0;
        if (GetRawInputData(rawInputHandle, RawInputCommand, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInputHandle, RawInputCommand, buffer, ref size, headerSize) != size ||
                (uint)Marshal.ReadInt32(buffer) != KeyboardInput)
            {
                return false;
            }

            // RAWKEYBOARD: MakeCode, Flags, Reserved, VKey (all 16-bit), right after the header.
            var keyboard = buffer + (int)headerSize;
            var makeCode = (ushort)Marshal.ReadInt16(keyboard, 0);
            var flags = (ushort)Marshal.ReadInt16(keyboard, 2);
            var reportedKey = (ushort)Marshal.ReadInt16(keyboard, 6);
            var isE0 = (flags & KeyE0) != 0;
            // 0xFF marks keys Windows fakes; some keyboards also wrap numpad keys in an extra Shift carrying the
            // E0 flag, which a real Shift press never has.
            if (reportedKey is 0 or 0xFF || (reportedKey == 0x10 && isE0))
            {
                return false;
            }

            virtualKey = Normalize(reportedKey, makeCode, isE0);
            isKeyDown = (flags & KeyBreak) == 0;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        var device = new RawInputDevice
        {
            UsagePage = GenericDesktopPage,
            Usage = KeyboardUsage,
            Flags = RemoveDevice,
            Target = IntPtr.Zero
        };
        _ = RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>());
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
}
