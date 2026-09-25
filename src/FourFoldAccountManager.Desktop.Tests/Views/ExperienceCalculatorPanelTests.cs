using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Tracking;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class ExperienceCalculatorPanelTests
{
    [Fact]
    public void TargetIsManualAndSurvivesOnlySameAccountRefresh() => WpfTestHost.Run(() =>
    {
        var panel = new ExperienceCalculatorPanel();
        var first = AccountProfile.Create("First");
        var second = AccountProfile.Create("Second");
        panel.SetAccounts([first, second]);

        panel.SetSnapshot(first, Snapshot(5));
        Assert.Equal(string.Empty, panel.TargetLevelBox.Text);
        Assert.Equal("—", panel.RemainingText.Text);

        panel.TargetLevelBox.Text = "3";
        Assert.Equal("25", panel.RemainingText.Text);

        panel.SetSnapshot(first, Snapshot(10));
        Assert.Equal("3", panel.TargetLevelBox.Text);
        Assert.Equal("20", panel.RemainingText.Text);

        panel.TargetLevelBox.Clear();
        Assert.Equal("—", panel.RemainingText.Text);

        panel.TargetLevelBox.Text = "3";
        panel.SetSnapshot(second, Snapshot(5));
        Assert.Equal(string.Empty, panel.TargetLevelBox.Text);
        Assert.Equal("—", panel.RemainingText.Text);
    });

    private static PlayerProgressSnapshot Snapshot(long currentXp) =>
        new("Alice", "Warrior", new Dictionary<string, ClassProfileSnapshot>
        {
            ["Warrior"] = new(2, currentXp, 30, null) { ClassName = "Warrior" }
        }, []);
}
