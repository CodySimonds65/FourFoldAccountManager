using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class FullscreenOverlayTrayTests
{
    [Fact]
    public void SwitchAutomationNamesDistinguishAccountsSharingTheSameLabel() => WpfTestHost.Run(() =>
    {
        var tray = new FullscreenOverlayTray();
        var alice = Row("Alice");
        var bob = Row("Bob");
        tray.SetRows([], [alice, bob]);
        tray.SetFullscreen(true);
        tray.SetEditing(true);
        tray.Measure(new Size(400, 800));
        tray.Arrange(new Rect(0, 0, 400, 800));
        tray.UpdateLayout();

        var names = FindCheckBoxes(tray.AccountRowsControl)
            .Select(AutomationProperties.GetName)
            .ToArray();

        Assert.Equal(["Alice XP/hr", "Bob XP/hr"], names);
    });

    private static IEnumerable<CheckBox> FindCheckBoxes(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is CheckBox checkBox)
            {
                yield return checkBox;
            }

            foreach (var descendant in FindCheckBoxes(child))
            {
                yield return descendant;
            }
        }
    }

    [Fact]
    public void GlobalSectionIsShownOnlyWhenGlobalSwitchesExist() => WpfTestHost.Run(() =>
    {
        var tray = new FullscreenOverlayTray();

        tray.SetRows([], [Row("Alice")]);
        Assert.Equal(Visibility.Collapsed, tray.GlobalSection.Visibility);

        tray.SetRows([new OverlayTraySwitch(new OverlayCardKey(OverlayAddOnKind.Xp, null), "Test", "", false, "Test")], []);
        Assert.Equal(Visibility.Visible, tray.GlobalSection.Visibility);
        Assert.Single(tray.GlobalSwitchesControl.Items);
    });

    [Fact]
    public void RowsAreReplacedOnEverySetRowsCall() => WpfTestHost.Run(() =>
    {
        var tray = new FullscreenOverlayTray();
        var first = Row("Alice", enabled: true);

        tray.SetRows([], [first, Row("Bob")]);
        Assert.Equal(2, tray.AccountRowsControl.Items.Count);

        var reverted = first with { Switches = [first.Switches[0] with { IsEnabled = false }] };
        tray.SetRows([], [reverted]);
        Assert.Equal(reverted, Assert.Single(tray.AccountRowsControl.Items.Cast<OverlayTrayAccountRow>()));
    });

    [Fact]
    public void ToggleRequestsAreRaisedOnlyWhileEditing() => WpfTestHost.Run(() =>
    {
        var tray = new FullscreenOverlayTray();
        var key = new OverlayCardKey(OverlayAddOnKind.Xp, Guid.NewGuid());
        OverlayCardToggleRequestedEventArgs? requested = null;
        tray.CardToggleRequested += (_, args) => requested = args;
        tray.SetFullscreen(true);

        tray.RequestToggle(key, true);
        Assert.Null(requested);

        tray.SetEditing(true);
        tray.RequestToggle(key, true);
        Assert.NotNull(requested);
        Assert.Equal(key, requested.Key);
        Assert.True(requested.Enabled);
    });

    private static OverlayTrayAccountRow Row(string label, bool enabled = false)
    {
        var accountId = Guid.NewGuid();
        return new OverlayTrayAccountRow(accountId, label,
            [new OverlayTraySwitch(new OverlayCardKey(OverlayAddOnKind.Xp, accountId), "XP/hr", "1 XP/hr", enabled,
                $"{label} XP/hr")]);
    }
}
