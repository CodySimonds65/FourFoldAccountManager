using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

[Flags]
public enum GlobalHotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4
}

public sealed record GlobalHotkeyChord(
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.Strict)] ushort VirtualKey,
    [property: JsonRequired] GlobalHotkeyModifiers Modifiers)
{
    // Mouse inputs share the key field. The buttons use their Windows virtual-key codes; the wheel has none, so
    // its two directions sit just past the keyboard range, where no key can ever collide with them.
    public const ushort MiddleClick = 0x04;
    public const ushort MouseButton4 = 0x05;
    public const ushort MouseButton5 = 0x06;
    public const ushort ScrollUp = 0x100;
    public const ushort ScrollDown = 0x101;

    private const GlobalHotkeyModifiers SupportedModifiers =
        GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt | GlobalHotkeyModifiers.Shift;

    public static GlobalHotkeyChord DefaultRevealXpOverlayTab { get; } =
        new(0x4F, SupportedModifiers);

    public static GlobalHotkeyChord DefaultToggleDividerResizing { get; } =
        new(0x4C, SupportedModifiers);

    public static GlobalHotkeyChord DefaultTimerSplit { get; } =
        new(0x53, SupportedModifiers);

    public static GlobalHotkeyChord DefaultTimerFinish { get; } =
        new(0x46, SupportedModifiers);

    public static GlobalHotkeyChord DefaultTimerReset { get; } =
        new(0x52, SupportedModifiers);

    public static GlobalHotkeyChord DefaultNextTab { get; } =
        new(0x27, SupportedModifiers);

    public static GlobalHotkeyChord DefaultPreviousTab { get; } =
        new(0x25, SupportedModifiers);

    public static GlobalHotkeyChord DefaultToggleTheatreMode { get; } =
        new(0x54, SupportedModifiers);

    // Any key except the Windows keys, the modifier keys, and F5, with or without Ctrl, Alt, or Shift. F5 stays
    // a browser refresh for the game panels. Esc, Tab, Enter, and Backspace sit below 0x20 and stay excluded
    // because they drive the Settings dialog. Also the middle and side mouse buttons and the wheel; left and
    // right click stay excluded because they would fire on every click.
    [JsonIgnore]
    public bool IsValid =>
        (IsMouse ||
         (VirtualKey is >= 0x20 and <= 0xFE || VirtualKey == 0x13) &&
         VirtualKey is not (0x5B or 0x5C or 0x74 or >= 0xA0 and <= 0xA5)) &&
        (Modifiers & ~SupportedModifiers) == 0;

    [JsonIgnore]
    public bool IsMouse => IsMouseInput(VirtualKey);

    // A key with no Ctrl, Alt, or Shift, or any mouse input. FourFold listens for these through raw input and
    // never takes them from the game or other apps; only key chords with a modifier are registered with
    // Windows, which cannot register a mouse button at all.
    [JsonIgnore]
    public bool IsObserved => IsMouse || Modifiers == GlobalHotkeyModifiers.None;

    public static bool IsMouseInput(ushort input) =>
        input is MiddleClick or MouseButton4 or MouseButton5 or ScrollUp or ScrollDown;

    public static bool TryCreate(
        ushort virtualKey,
        GlobalHotkeyModifiers modifiers,
        out GlobalHotkeyChord chord)
    {
        chord = new GlobalHotkeyChord(virtualKey, modifiers);
        return chord.IsValid;
    }
}
