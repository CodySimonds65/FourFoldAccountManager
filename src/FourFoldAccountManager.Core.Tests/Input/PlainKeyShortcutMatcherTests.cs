using FourFoldAccountManager.Core.Input;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Input;

public sealed class PlainKeyShortcutMatcherTests
{
    private const ushort S = 0x53;
    private const ushort LeftShift = 0xA0;
    private const ushort LeftControl = 0xA2;

    [Fact]
    public void HeldModifiersStillFireThePlainKeyUnlessTheComboIsAnotherShortcut()
    {
        var matcher = new PlainKeyShortcutMatcher();
        matcher.SetBindings(new Dictionary<GlobalShortcutAction, GlobalHotkeyChord>
        {
            [GlobalShortcutAction.TimerSplit] = new(S, GlobalHotkeyModifiers.None),
            [GlobalShortcutAction.TimerReset] = new(S, GlobalHotkeyModifiers.Shift)
        });

        // Shift+S is the reset chord, so the plain split must not fire alongside it and lose the run.
        Assert.Null(matcher.KeyDown(LeftShift));
        Assert.Null(matcher.KeyDown(S));
        matcher.KeyUp(S);
        matcher.KeyUp(LeftShift);

        // Ctrl+S is not a shortcut, so holding Ctrl does not block the plain split.
        Assert.Null(matcher.KeyDown(LeftControl));
        Assert.Equal(GlobalShortcutAction.TimerSplit, matcher.KeyDown(S));
    }

    [Fact]
    public void HoldingAPlainKeyFiresOnceUntilItIsReleased()
    {
        var matcher = new PlainKeyShortcutMatcher();
        matcher.SetBindings(new Dictionary<GlobalShortcutAction, GlobalHotkeyChord>
        {
            [GlobalShortcutAction.TimerSplit] = new(S, GlobalHotkeyModifiers.None)
        });

        Assert.Equal(GlobalShortcutAction.TimerSplit, matcher.KeyDown(S));
        // Windows repeats key-down while a key is held; each repeat would otherwise record another lap.
        Assert.Null(matcher.KeyDown(S));
        Assert.Null(matcher.KeyDown(S));
        matcher.KeyUp(S);

        Assert.Equal(GlobalShortcutAction.TimerSplit, matcher.KeyDown(S));
    }
}
