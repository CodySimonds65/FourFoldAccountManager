using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Desktop.Services;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Services;

public sealed class RawInputListenerTests
{
    private const ushort LeftDown = 0x0001;
    private const ushort RightDown = 0x0004;
    private const ushort MiddleDown = 0x0010;
    private const ushort MiddleUp = 0x0020;
    private const ushort Button4Down = 0x0040;
    private const ushort Button5Up = 0x0200;
    private const ushort Wheel = 0x0400;
    private const ushort HorizontalWheel = 0x0800;

    [Fact]
    public void MouseButtonsDecodeToTheirShortcutCodes()
    {
        Assert.Equal([(GlobalHotkeyChord.MiddleClick, true)], Decode(MiddleDown));
        Assert.Equal([(GlobalHotkeyChord.MiddleClick, false)], Decode(MiddleUp));
        // One raw event can carry several button changes.
        Assert.Equal(
            [(GlobalHotkeyChord.MouseButton4, true), (GlobalHotkeyChord.MouseButton5, false)],
            Decode(Button4Down | Button5Up));
    }

    // The wheel has no release, so each notch is a press followed at once by its release; otherwise the second
    // notch would look like a held key and never fire.
    [Fact]
    public void EachWheelNotchDecodesToAPressAndReleaseInItsDirection()
    {
        Assert.Equal([(GlobalHotkeyChord.ScrollUp, true), (GlobalHotkeyChord.ScrollUp, false)], Decode(Wheel, 120));
        Assert.Equal([(GlobalHotkeyChord.ScrollDown, true), (GlobalHotkeyChord.ScrollDown, false)], Decode(Wheel, -120));
    }

    // Smooth-scrolling wheels report a notch in pieces; firing on every piece would skip several tabs at once.
    [Fact]
    public void ASmoothWheelFiresOncePerFullNotchAndStartsOverWhenItTurnsBack()
    {
        var remainder = 0;

        Assert.Empty(Decode(Wheel, 60, ref remainder));
        Assert.Equal(
            [(GlobalHotkeyChord.ScrollUp, true), (GlobalHotkeyChord.ScrollUp, false)], Decode(Wheel, 60, ref remainder));
        Assert.Empty(Decode(Wheel, 90, ref remainder));
        // Turning back must not be cancelled out by the 90 still pending the other way.
        Assert.Equal(
            [(GlobalHotkeyChord.ScrollDown, true), (GlobalHotkeyChord.ScrollDown, false)],
            Decode(Wheel, -120, ref remainder));
    }

    [Fact]
    public void LeftClickRightClickSidewaysScrollAndMovementDecodeToNothing()
    {
        Assert.Empty(Decode(LeftDown | RightDown));
        Assert.Empty(Decode(HorizontalWheel, 120));
        Assert.Empty(Decode(0));
    }

    private static List<(ushort Input, bool IsDown)> Decode(int buttonFlags, short wheelDelta = 0)
    {
        var remainder = 0;
        return Decode(buttonFlags, wheelDelta, ref remainder);
    }

    private static List<(ushort Input, bool IsDown)> Decode(int buttonFlags, short wheelDelta, ref int remainder)
    {
        var inputs = new List<(ushort, bool)>();
        RawInputListener.DecodeMouse(
            (ushort)buttonFlags, wheelDelta, ref remainder, (input, isDown) => inputs.Add((input, isDown)));
        return inputs;
    }
}
