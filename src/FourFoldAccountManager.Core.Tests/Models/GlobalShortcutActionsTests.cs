using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Models;

public sealed class GlobalShortcutActionsTests
{
    private static readonly GlobalHotkeyChord NumPad1 = new(0x61, GlobalHotkeyModifiers.None);

    [Fact]
    public void AllListsActionsInPriorityOrder() =>
        Assert.Equal(
        [
            GlobalShortcutAction.RevealOverlays,
            GlobalShortcutAction.ToggleDividerResizing,
            GlobalShortcutAction.TimerSplit,
            GlobalShortcutAction.TimerFinish,
            GlobalShortcutAction.TimerReset
        ], GlobalShortcutActions.All);

    [Fact]
    public void GetChordReadsEachActionsSetting()
    {
        var settings = PanelSettings.Default;

        Assert.Equal(settings.RevealXpOverlayTabShortcut,
            GlobalShortcutActions.GetChord(settings, GlobalShortcutAction.RevealOverlays));
        Assert.Equal(settings.ToggleDividerResizingShortcut,
            GlobalShortcutActions.GetChord(settings, GlobalShortcutAction.ToggleDividerResizing));
        Assert.Equal(settings.TimerSplitShortcut,
            GlobalShortcutActions.GetChord(settings, GlobalShortcutAction.TimerSplit));
        Assert.Equal(settings.TimerFinishShortcut,
            GlobalShortcutActions.GetChord(settings, GlobalShortcutAction.TimerFinish));
        Assert.Equal(settings.TimerResetShortcut,
            GlobalShortcutActions.GetChord(settings, GlobalShortcutAction.TimerReset));
    }

    [Fact]
    public void WithChordChangesOnlyTheGivenAction()
    {
        foreach (var action in GlobalShortcutActions.All)
        {
            var changed = GlobalShortcutActions.WithChord(PanelSettings.Default, action, NumPad1);

            foreach (var other in GlobalShortcutActions.All)
            {
                Assert.Equal(other == action ? NumPad1 : GlobalShortcutActions.GetChord(PanelSettings.Default, other),
                    GlobalShortcutActions.GetChord(changed, other));
            }
        }
    }

    [Fact]
    public void DefaultShortcutsAreValidAndDistinct()
    {
        var defaults = GlobalShortcutActions.All.ToDictionary(
            action => action, action => GlobalShortcutActions.GetChord(PanelSettings.Default, action));

        Assert.All(defaults.Values, chord => Assert.True(chord.IsValid));
        Assert.Null(GlobalShortcutActions.FindDuplicate(defaults));
    }

    [Fact]
    public void FindDuplicateNamesTheEarlierActionFirst()
    {
        var chords = GlobalShortcutActions.All.ToDictionary(
            action => action, action => GlobalShortcutActions.GetChord(PanelSettings.Default, action));
        chords[GlobalShortcutAction.TimerFinish] = chords[GlobalShortcutAction.RevealOverlays];

        Assert.Equal((GlobalShortcutAction.RevealOverlays, GlobalShortcutAction.TimerFinish),
            GlobalShortcutActions.FindDuplicate(chords));
    }

    [Theory]
    [InlineData(GlobalShortcutAction.RevealOverlays, "Reveal overlays tab")]
    [InlineData(GlobalShortcutAction.ToggleDividerResizing, "Toggle divider resizing")]
    [InlineData(GlobalShortcutAction.TimerSplit, "Timer split")]
    [InlineData(GlobalShortcutAction.TimerFinish, "Timer finish")]
    [InlineData(GlobalShortcutAction.TimerReset, "Timer reset")]
    public void DisplayNamesMatchTheSettingsLabels(GlobalShortcutAction action, string expected) =>
        Assert.Equal(expected, GlobalShortcutActions.DisplayName(action));
}
