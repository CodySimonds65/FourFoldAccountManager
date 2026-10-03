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
}
