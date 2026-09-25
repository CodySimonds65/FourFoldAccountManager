using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Models;

public sealed class GlobalHotkeyChordTests
{
    private const GlobalHotkeyModifiers All =
        GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt | GlobalHotkeyModifiers.Shift;

    [Theory]
    [InlineData((ushort)0x13)] // Pause
    [InlineData((ushort)0x91)] // Scroll Lock
    [InlineData((ushort)0x2D)] // Insert
    [InlineData((ushort)0x60)] // Numpad 0
    [InlineData((ushort)0x69)] // Numpad 9
    [InlineData((ushort)0x6A)] // Numpad *
    [InlineData((ushort)0x6B)] // Numpad +
    [InlineData((ushort)0x6D)] // Numpad -
    [InlineData((ushort)0x6E)] // Numpad .
    [InlineData((ushort)0x6F)] // Numpad /
    [InlineData((ushort)0x7C)] // F13
    [InlineData((ushort)0x87)] // F24
    public void AllowListedKeysAreValidWithoutModifiers(ushort virtualKey)
    {
        Assert.True(GlobalHotkeyChord.CanBeBoundAlone(virtualKey));
        Assert.True(new GlobalHotkeyChord(virtualKey, GlobalHotkeyModifiers.None).IsValid);
    }

    [Theory]
    [InlineData((ushort)0x41)] // A
    [InlineData((ushort)0x31)] // 1
    [InlineData((ushort)0x70)] // F1
    [InlineData((ushort)0x20)] // Space
    [InlineData((ushort)0x6C)] // Numpad separator
    [InlineData((ushort)0x88)] // just past F24
    [InlineData((ushort)0x23)] // End (numpad 1 with Num Lock off)
    public void OtherKeysStillNeedAModifier(ushort virtualKey)
    {
        Assert.False(GlobalHotkeyChord.CanBeBoundAlone(virtualKey));
        Assert.False(new GlobalHotkeyChord(virtualKey, GlobalHotkeyModifiers.None).IsValid);
        Assert.True(new GlobalHotkeyChord(virtualKey, GlobalHotkeyModifiers.Control).IsValid);
    }

    [Fact]
    public void ExistingModifierRulesAreUnchanged()
    {
        Assert.True(new GlobalHotkeyChord(0x41, All).IsValid);
        Assert.False(new GlobalHotkeyChord(0x5B, GlobalHotkeyModifiers.Control).IsValid); // Windows key
        Assert.False(new GlobalHotkeyChord(0xA0, GlobalHotkeyModifiers.Shift).IsValid); // Left Shift
        Assert.False(new GlobalHotkeyChord(0x41, (GlobalHotkeyModifiers)8).IsValid);
        Assert.False(new GlobalHotkeyChord(0x1F, GlobalHotkeyModifiers.Control).IsValid);
        Assert.True(new GlobalHotkeyChord(0x13, GlobalHotkeyModifiers.Control).IsValid); // Ctrl+Pause
    }

    [Fact]
    public void TimerDefaultsUseAllModifiersWithSFAndR()
    {
        Assert.Equal(new GlobalHotkeyChord(0x53, All), GlobalHotkeyChord.DefaultTimerSplit);
        Assert.Equal(new GlobalHotkeyChord(0x46, All), GlobalHotkeyChord.DefaultTimerFinish);
        Assert.Equal(new GlobalHotkeyChord(0x52, All), GlobalHotkeyChord.DefaultTimerReset);
    }
}
