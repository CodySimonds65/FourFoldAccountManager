using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Tests;

public class XpProgressCalculatorTests
{
    private static PlayerProgressSnapshot Snapshot(int level, long current, long cap, ClassXpSnapshot? second = null) =>
        new("Desmond", "Scout", new Dictionary<string, ClassXpSnapshot>
        {
            ["Scout"] = new(level, current, cap, null),
            ["Shaman"] = second ?? new ClassXpSnapshot(1, 0, 10, null)
        }, []);

    [Fact]
    public void Same_level_gain_is_current_xp_difference()
    {
        var result = XpProgressCalculator.Calculate(Snapshot(13, 300, 910), Snapshot(13, 450, 910));
        Assert.Equal(150, result.ValidGain);
        Assert.Empty(result.InvalidClasses);
    }

    [Fact]
    public void Single_level_gain_includes_old_remainder_and_new_progress()
    {
        var result = XpProgressCalculator.Calculate(Snapshot(13, 900, 910), Snapshot(14, 100, 1050));
        Assert.Equal(110, result.ValidGain);
    }

    [Fact]
    public void Multiple_levels_include_each_crossed_cap()
    {
        var result = XpProgressCalculator.Calculate(Snapshot(13, 900, 910), Snapshot(15, 20, 1200));
        Assert.Equal(1080, result.ValidGain);
    }

    [Fact]
    public void Other_healthy_class_still_contributes_when_one_resets()
    {
        var before = Snapshot(13, 900, 910, new ClassXpSnapshot(1, 2, 10, null));
        var after = Snapshot(13, 100, 910, new ClassXpSnapshot(1, 7, 10, null));
        var result = XpProgressCalculator.Calculate(before, after);
        Assert.Equal(5, result.ValidGain);
        Assert.Contains("Scout", result.InvalidClasses);
    }

    [Theory]
    [InlineData(12, 50, 780)]
    [InlineData(13, 50, 999)]
    [InlineData(int.MaxValue, 50, long.MaxValue)]
    public void Invalid_level_or_cap_does_not_fabricate_gain(int nextLevel, long nextCurrent, long nextCap)
    {
        var result = XpProgressCalculator.Calculate(Snapshot(13, 900, 910), Snapshot(nextLevel, nextCurrent, nextCap));
        Assert.Equal(0, result.ValidGain);
        Assert.Contains("Scout", result.InvalidClasses);
    }

    [Fact]
    public void Missing_class_is_reported_without_discarding_healthy_gain()
    {
        var before = Snapshot(13, 300, 910);
        var after = new PlayerProgressSnapshot("Desmond", null, new Dictionary<string, ClassXpSnapshot>
        {
            ["Shaman"] = new(1, 4, 10, null)
        }, ["Scout"]);
        var result = XpProgressCalculator.Calculate(before, after);
        Assert.Equal(4, result.ValidGain);
        Assert.Contains("Scout", result.InvalidClasses);
    }
}
