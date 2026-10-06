using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Models;

public sealed class GlobalHotkeyChordTests
{
    [Theory]
    [InlineData(GlobalHotkeyChord.MiddleClick)]
    [InlineData(GlobalHotkeyChord.MouseButton4)]
    [InlineData(GlobalHotkeyChord.MouseButton5)]
    [InlineData(GlobalHotkeyChord.ScrollUp)]
    [InlineData(GlobalHotkeyChord.ScrollDown)]
    public void MouseInputsAreValidAndObservedWithOrWithoutModifiers(ushort mouseInput)
    {
        var plain = new GlobalHotkeyChord(mouseInput, GlobalHotkeyModifiers.None);
        var withModifiers = new GlobalHotkeyChord(mouseInput, GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Shift);

        Assert.True(plain.IsValid);
        Assert.True(withModifiers.IsValid);
        // Windows cannot register a mouse button as a hotkey, so even Ctrl+Middle click has to be observed.
        Assert.True(plain.IsObserved);
        Assert.True(withModifiers.IsObserved);
    }

    // Left and right click would fire on every click; F5 must stay the game panels' refresh.
    [Theory]
    [InlineData(0x01)] // Left click
    [InlineData(0x02)] // Right click
    [InlineData(0x03)] // Ctrl+Break
    [InlineData(0x74)] // F5
    [InlineData(0x102)] // One past the scroll codes
    public void LeftClickRightClickAndF5StayInvalid(ushort virtualKey)
    {
        Assert.False(new GlobalHotkeyChord(virtualKey, GlobalHotkeyModifiers.None).IsValid);
        Assert.False(new GlobalHotkeyChord(virtualKey, GlobalHotkeyModifiers.Control).IsValid);
    }

    [Fact]
    public void AKeyWithModifiersIsStillRegisteredWithWindowsRatherThanObserved()
    {
        Assert.False(new GlobalHotkeyChord(0x53, GlobalHotkeyModifiers.Control).IsObserved);
        Assert.True(new GlobalHotkeyChord(0x53, GlobalHotkeyModifiers.None).IsObserved);
    }
}
