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

    // Keys a speedrunner can bind alone. Every other key needs Ctrl, Alt, or Shift so ordinary typing keeps working.
    private static readonly HashSet<ushort> SingleKeys = BuildSingleKeys();

    [JsonIgnore]
    public bool IsValid =>
        (VirtualKey is >= 0x20 and <= 0xFE || VirtualKey == 0x13) &&
        VirtualKey is not (0x5B or 0x5C or >= 0xA0 and <= 0xA5) &&
        (Modifiers & ~SupportedModifiers) == 0 &&
        (Modifiers != GlobalHotkeyModifiers.None || CanBeBoundAlone(VirtualKey));

    public static bool CanBeBoundAlone(ushort virtualKey) => SingleKeys.Contains(virtualKey);

    public static bool TryCreate(
        ushort virtualKey,
        GlobalHotkeyModifiers modifiers,
        out GlobalHotkeyChord chord)
    {
        chord = new GlobalHotkeyChord(virtualKey, modifiers);
        return chord.IsValid;
    }

    private static HashSet<ushort> BuildSingleKeys()
    {
        // Pause, Scroll Lock, Insert, and numpad * + - . /
        var keys = new HashSet<ushort> { 0x13, 0x91, 0x2D, 0x6A, 0x6B, 0x6D, 0x6E, 0x6F };
        for (ushort key = 0x60; key <= 0x69; key++)
        {
            keys.Add(key); // Numpad 0-9
        }

        for (ushort key = 0x7C; key <= 0x87; key++)
        {
            keys.Add(key); // F13-F24
        }

        return keys;
    }
}
