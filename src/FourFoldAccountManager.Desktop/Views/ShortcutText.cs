using System.Windows.Input;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

internal static class ShortcutText
{
    public static string Format(GlobalHotkeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        var parts = new List<string>();
        if (chord.Modifiers.HasFlag(GlobalHotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (chord.Modifiers.HasFlag(GlobalHotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (chord.Modifiers.HasFlag(GlobalHotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(KeyName(chord.VirtualKey));
        return string.Join("+", parts);
    }

    private static string KeyName(ushort virtualKey)
    {
        var key = KeyInterop.KeyFromVirtualKey(virtualKey);
        var name = key switch
        {
            Key.Scroll => "Scroll Lock",
            Key.Multiply => "Num *",
            Key.Add => "Num +",
            Key.Subtract => "Num -",
            Key.Decimal => "Num .",
            Key.Divide => "Num /",
            >= Key.NumPad0 and <= Key.NumPad9 => $"Num {key - Key.NumPad0}",
            _ => key.ToString()
        };
        return name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]) ? name[1].ToString() : name;
    }
}
