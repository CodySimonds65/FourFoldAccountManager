using System.Globalization;
using System.Text;

namespace FourFoldAccountManager.Core.Plugins;

public static class PluginText
{
    // Text a plugin supplies is drawn by FourFold, so it can't carry anything that rearranges or hides the text around
    // it: control characters (a line break, a bell), direction overrides, zero-width spaces and the line and paragraph
    // separators. U+200D, the zero-width joiner, is allowed because emoji use it.
    public static bool HasUnsafeCharacter(string text) =>
        text.EnumerateRunes().Any(rune => Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.Format => rune.Value != 0x200D,
            UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => true,
            _ => false
        });
}
