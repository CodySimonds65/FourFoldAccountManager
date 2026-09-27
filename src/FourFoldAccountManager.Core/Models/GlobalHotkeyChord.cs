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

public sealed record GlobalHotkeyChord(ushort VirtualKey, GlobalHotkeyModifiers Modifiers)
{
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

    // Any key except the Windows and modifier keys, with or without Ctrl, Alt, or Shift. Esc, Tab, Enter,
    // and Backspace sit below 0x20 and stay excluded because they drive the Settings dialog.
    [JsonIgnore]
    public bool IsValid =>
        (VirtualKey is >= 0x20 and <= 0xFE || VirtualKey == 0x13) &&
        VirtualKey is not (0x5B or 0x5C or >= 0xA0 and <= 0xA5) &&
        (Modifiers & ~SupportedModifiers) == 0;

    // A key with no Ctrl, Alt, or Shift. FourFold listens for it through raw input and never takes it from
    // the game or other apps; only modifier chords are registered with Windows.
    [JsonIgnore]
    public bool IsPlainKey => Modifiers == GlobalHotkeyModifiers.None;

    public static bool TryCreate(
        ushort virtualKey,
        GlobalHotkeyModifiers modifiers,
        out GlobalHotkeyChord chord)
    {
        chord = new GlobalHotkeyChord(virtualKey, modifiers);
        return chord.IsValid;
    }
}
