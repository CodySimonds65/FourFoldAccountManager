using System.Windows;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class SettingsDialogTests
{
    private static readonly GlobalHotkeyChord NumPad1 = new(0x61, GlobalHotkeyModifiers.None);

    [Fact]
    public void RowsShowEachActionsKeysAndAvailability() => WpfTestHost.Run(() =>
    {
        var shortcuts = Defaults();
        shortcuts[GlobalShortcutAction.TimerSplit] = NumPad1;
        var dialog = new SettingsDialog(false, true, shortcuts,
            new HashSet<GlobalShortcutAction> { GlobalShortcutAction.TimerReset });

        Assert.Equal("Num 1", dialog.RowFor(GlobalShortcutAction.TimerSplit).CaptureButton.Content);
        Assert.Equal("Ctrl+Alt+Shift+O", dialog.RowFor(GlobalShortcutAction.RevealOverlays).CaptureButton.Content);
        Assert.StartsWith("Unavailable", dialog.RowFor(GlobalShortcutAction.TimerReset).StatusText.Text);
        Assert.StartsWith("Available globally", dialog.RowFor(GlobalShortcutAction.TimerFinish).StatusText.Text);
        Assert.Equal(shortcuts, dialog.Shortcuts);
    });

    [Fact]
    public void CapturingASingleNumpadKeyUpdatesTheShortcut() => WpfTestHost.Run(() =>
    {
        var dialog = new SettingsDialog(false, true, Defaults(), new HashSet<GlobalShortcutAction>());

        dialog.BeginCapturingShortcut(GlobalShortcutAction.TimerSplit);
        Assert.True(dialog.TryApplyCapturedKey(0x61, GlobalHotkeyModifiers.None));

        Assert.Equal(NumPad1, dialog.Shortcuts[GlobalShortcutAction.TimerSplit]);
        var row = dialog.RowFor(GlobalShortcutAction.TimerSplit);
        Assert.Equal("Num 1", row.CaptureButton.Content);
        Assert.Equal("Save settings to register this shortcut.", row.StatusText.Text);
    });

    [Fact]
    public void CapturingANavigationKeyWithoutModifiersIsRejectedWithNumLockHint() => WpfTestHost.Run(() =>
    {
        var dialog = new SettingsDialog(false, true, Defaults(), new HashSet<GlobalShortcutAction>());

        dialog.BeginCapturingShortcut(GlobalShortcutAction.TimerSplit);
        Assert.False(dialog.TryApplyCapturedKey(0x23, GlobalHotkeyModifiers.None)); // End: numpad 1 with Num Lock off

        Assert.Equal(GlobalHotkeyChord.DefaultTimerSplit, dialog.Shortcuts[GlobalShortcutAction.TimerSplit]);
        var status = dialog.RowFor(GlobalShortcutAction.TimerSplit).StatusText.Text;
        Assert.Contains("Use Ctrl, Alt, or Shift", status);
        Assert.Contains("Num Lock on", status);
    });

    [Fact]
    public void DuplicateShortcutsAreReportedByName() => WpfTestHost.Run(() =>
    {
        var dialog = new SettingsDialog(false, true, Defaults(), new HashSet<GlobalShortcutAction>());
        Assert.Null(dialog.DuplicateShortcutMessage());

        dialog.BeginCapturingShortcut(GlobalShortcutAction.TimerFinish);
        dialog.TryApplyCapturedKey(0x4F,
            GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt | GlobalHotkeyModifiers.Shift);

        Assert.Equal(
            "Reveal overlays tab and Timer finish use the same keys. Choose a different shortcut for one of them.",
            dialog.DuplicateShortcutMessage());
    });

    [Fact]
    public void DialogIsCappedToTheScreenAndScrolls() => WpfTestHost.Run(() =>
    {
        var dialog = new SettingsDialog(false, true, Defaults(), new HashSet<GlobalShortcutAction>());

        Assert.Equal(SystemParameters.WorkArea.Height, dialog.MaxHeight);
        Assert.NotNull(dialog.SettingsScrollViewer);
    });

    private static Dictionary<GlobalShortcutAction, GlobalHotkeyChord> Defaults() =>
        GlobalShortcutActions.All.ToDictionary(
            action => action, action => GlobalShortcutActions.GetChord(PanelSettings.Default, action));
}
