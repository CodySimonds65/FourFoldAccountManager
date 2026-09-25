using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Calculation;

public sealed class XpCalculatorTargetsTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    [Fact]
    public void WithTargetAddsReplacesAndRemovesOnlyThatAccount()
    {
        var settings = XpCalculatorTargets.WithTarget(PanelSettings.Default, Alice, 50);
        settings = XpCalculatorTargets.WithTarget(settings, Bob, 20);
        settings = XpCalculatorTargets.WithTarget(settings, Alice, 60);

        Assert.Equal(60, XpCalculatorTargets.Get(settings, Alice));
        Assert.Equal(20, XpCalculatorTargets.Get(settings, Bob));

        settings = XpCalculatorTargets.WithTarget(settings, Alice, null);

        Assert.Null(XpCalculatorTargets.Get(settings, Alice));
        Assert.Equal(20, XpCalculatorTargets.Get(settings, Bob));
    }

    [Fact]
    public void WithTargetRejectsNonPositiveLevelsAndEmptyAccounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => XpCalculatorTargets.WithTarget(PanelSettings.Default, Alice, 0));
        Assert.Throws<ArgumentException>(() => XpCalculatorTargets.WithTarget(PanelSettings.Default, Guid.Empty, 5));
    }

    [Fact]
    public void WithTargetThrowsAboveTheCapAndAcceptsTheCap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            XpCalculatorTargets.WithTarget(PanelSettings.Default, Alice, XpCalculatorTargets.MaxTargetLevel + 1));

        var settings = XpCalculatorTargets.WithTarget(PanelSettings.Default, Alice, XpCalculatorTargets.MaxTargetLevel);

        Assert.Equal(XpCalculatorTargets.MaxTargetLevel, XpCalculatorTargets.Get(settings, Alice));
    }

    [Fact]
    public void NormalizeDropsEmptyAccountsAndNonPositiveLevels()
    {
        var normalized = XpCalculatorTargets.Normalize(new Dictionary<Guid, long>
        {
            [Alice] = 50,
            [Bob] = 0,
            [Guid.Empty] = 7
        });

        Assert.Equal(new Dictionary<Guid, long> { [Alice] = 50 }, normalized);
        Assert.Empty(XpCalculatorTargets.Normalize(null));
    }

    [Fact]
    public void NormalizeDropsEntriesAboveTheCap()
    {
        var normalized = XpCalculatorTargets.Normalize(new Dictionary<Guid, long>
        {
            [Alice] = 50,
            [Bob] = XpCalculatorTargets.MaxTargetLevel + 1
        });

        Assert.Equal(new Dictionary<Guid, long> { [Alice] = 50 }, normalized);
    }

    [Fact]
    public void RemoveAccountDropsOnlyThatAccount()
    {
        var remaining = XpCalculatorTargets.RemoveAccount(
            new Dictionary<Guid, long> { [Alice] = 50, [Bob] = 20 }, Alice);

        Assert.Equal(new Dictionary<Guid, long> { [Bob] = 20 }, remaining);
    }
}
