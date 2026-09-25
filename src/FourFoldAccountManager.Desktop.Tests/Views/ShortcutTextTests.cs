using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class ShortcutTextTests
{
    [Theory]
    [InlineData((ushort)0x53, GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt | GlobalHotkeyModifiers.Shift,
        "Ctrl+Alt+Shift+S")]
    [InlineData((ushort)0x61, GlobalHotkeyModifiers.None, "Num 1")]
    [InlineData((ushort)0x60, GlobalHotkeyModifiers.None, "Num 0")]
    [InlineData((ushort)0x6A, GlobalHotkeyModifiers.None, "Num *")]
    [InlineData((ushort)0x6F, GlobalHotkeyModifiers.None, "Num /")]
    [InlineData((ushort)0x7C, GlobalHotkeyModifiers.None, "F13")]
    [InlineData((ushort)0x91, GlobalHotkeyModifiers.None, "Scroll Lock")]
    [InlineData((ushort)0x13, GlobalHotkeyModifiers.None, "Pause")]
    [InlineData((ushort)0x31, GlobalHotkeyModifiers.Control, "Ctrl+1")]
    public void FormatsShortcutsForPeople(ushort virtualKey, GlobalHotkeyModifiers modifiers, string expected) =>
        Assert.Equal(expected, ShortcutText.Format(new GlobalHotkeyChord(virtualKey, modifiers)));
}
