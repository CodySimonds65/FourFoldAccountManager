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

    public bool IsValid =>
        VirtualKey is >= 0x20 and <= 0xFE &&
        VirtualKey is not (0x5B or 0x5C or >= 0xA0 and <= 0xA5) &&
        Modifiers != GlobalHotkeyModifiers.None &&
        (Modifiers & ~SupportedModifiers) == 0;

    public static bool TryCreate(
        ushort virtualKey,
        GlobalHotkeyModifiers modifiers,
        out GlobalHotkeyChord chord)
    {
        chord = new GlobalHotkeyChord(virtualKey, modifiers);
        return chord.IsValid;
    }
}
