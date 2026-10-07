using System.Globalization;
using System.Text;

namespace FourFoldAccountManager.Core.Plugins;

public static class PluginText
{
    // Text a plugin supplies is drawn by FourFold, so it can't carry anything that rearranges or hides the text around
    // it: control characters (a line break, a bell), direction overrides, zero-width spaces and the line and paragraph
    // separators. The zero-width joiner and non-joiner (U+200D, U+200C) are allowed: emoji and several scripts
    // (Persian, for one) need them.
    public static bool HasUnsafeCharacter(string text) =>
        text.EnumerateRunes().Any(rune => Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.Format => rune.Value is not (0x200C or 0x200D),
            UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => true,
            _ => false
        });

    // Text the game wrote (a location, an item's name, a scene), made safe to hand to a plugin: trimmed, at most 64
    // characters, and dropped if it could rearrange or hide the text a plugin draws around it.
    public static string? PublicGameText(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || HasUnsafeCharacter(text))
        {
            return null;
        }

        // Never cut between the two halves of a character outside the basic plane.
        return text.Length <= 64 ? text : text[..(char.IsHighSurrogate(text[63]) ? 63 : 64)];
    }
}
