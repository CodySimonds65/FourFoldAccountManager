using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Tracking;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class ExperienceCalculatorTargetTests
{
    [Fact]
    public void InvalidTextKeepsTheSavedTargetAndClearingRemovesIt() => WpfTestHost.Run(() =>
    {
        var (panel, first, _) = CreatePanel();
        var saves = new List<(Guid, long?)>();
        panel.TargetLevelChanged += (accountId, level) => saves.Add((accountId, level));
        panel.SetSnapshot(first, Snapshot(), savedTargetLevel: 3);

        panel.TargetLevelBox.Text = "abc";
        panel.FlushPendingTargetSave();
        Assert.Empty(saves);

        panel.TargetLevelBox.Text = string.Empty;
        panel.FlushPendingTargetSave();
        Assert.Equal([(first.Id, (long?)null)], saves);
    });

    [Fact]
    public void SwitchingAccountsSavesThePendingTargetForThePreviousAccount() => WpfTestHost.Run(() =>
    {
        var (panel, first, second) = CreatePanel();
        var saves = new List<(Guid, long?)>();
        panel.TargetLevelChanged += (accountId, level) => saves.Add((accountId, level));
        panel.SetSnapshot(first, Snapshot());

        panel.TargetLevelBox.Text = "7";
        panel.SetSnapshot(second, Snapshot(), savedTargetLevel: 4);

        Assert.Equal([(first.Id, (long?)7)], saves);
        Assert.Equal("4", panel.TargetLevelBox.Text);
    });

    [Fact]
    public void TypingAboveTheCapSchedulesNoSave() => WpfTestHost.Run(() =>
    {
        var (panel, first, _) = CreatePanel();
        var saves = new List<(Guid, long?)>();
        panel.TargetLevelChanged += (accountId, level) => saves.Add((accountId, level));
        panel.SetSnapshot(first, Snapshot());

        panel.TargetLevelBox.Text = "10000";
        panel.FlushPendingTargetSave();

        Assert.Empty(saves);
    });

    private static (ExperienceCalculatorPanel Panel, AccountProfile First, AccountProfile Second) CreatePanel()
    {
        var panel = new ExperienceCalculatorPanel();
        var first = AccountProfile.Create("First");
        var second = AccountProfile.Create("Second");
        panel.SetAccounts([first, second]);
        return (panel, first, second);
    }

    private static PlayerProgressSnapshot Snapshot() =>
        new("Alice", "Warrior", new Dictionary<string, ClassProfileSnapshot>
        {
            ["Warrior"] = new(2, 5, 30, null) { ClassName = "Warrior" }
        }, []);
}
