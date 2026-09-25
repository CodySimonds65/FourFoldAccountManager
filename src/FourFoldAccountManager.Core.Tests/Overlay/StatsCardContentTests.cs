using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Tracking;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Overlay;

public sealed class StatsCardContentTests
{
    [Fact]
    public void NoSnapshotWaitsForTheProfile()
    {
        var content = StatsCardContent.FromSnapshot(null);

        Assert.False(content.IsAvailable);
        Assert.Equal("Waiting for profile…", content.Status);
        Assert.Empty(content.Rows);
        Assert.Equal(string.Empty, content.CountsText);
    }

    [Fact]
    public void AvailableComparisonBuildsHeaderSummaryAndRows()
    {
        var state = new ClassComparisonDisplayState(true, "Profile stats loaded.", "Mage", 42, null,
        [
            new ClassStatComparison(CharacterStat.Hp, 1_100, 1_000, 100, 10.0, ComparisonDirection.Above),
            new ClassStatComparison(CharacterStat.Defense, 90, 100, -10, -10.0, ComparisonDirection.Below),
            new ClassStatComparison(CharacterStat.Luck, 50, 50, 0, 0.0, ComparisonDirection.Equal)
        ], 1, 1, 1, 0.0);

        var content = StatsCardContent.FromState(state);

        Assert.True(content.IsAvailable);
        Assert.Equal("Mage 42", content.Header);
        Assert.Equal("▲1 ▼1", content.CountsText);
        Assert.Equal("▲1 ▼1 · mean 0.0%", content.SummaryText);
        Assert.Equal(
        [
            new StatsCardRow("HP", "1,100", "1,000", "+10.0%", ComparisonDirection.Above),
            new StatsCardRow("DEF", "90", "100", "-10.0%", ComparisonDirection.Below),
            new StatsCardRow("LCK", "50", "50", "0.0%", ComparisonDirection.Equal)
        ], content.Rows);
    }

    [Fact]
    public void UnavailableComparisonShowsItsStatus()
    {
        var noClass = new PlayerProgressSnapshot("Alice", null, new Dictionary<string, ClassProfileSnapshot>(), []);
        var unknownClass = new PlayerProgressSnapshot("Alice", "Nobody", new Dictionary<string, ClassProfileSnapshot>
        {
            ["Nobody"] = new(3, 0, 10, null) { ClassName = "Nobody" }
        }, []);

        var missing = StatsCardContent.FromSnapshot(noClass);
        var unknown = StatsCardContent.FromSnapshot(unknownClass);

        Assert.False(missing.IsAvailable);
        Assert.Equal("The active class is unavailable.", missing.Status);
        Assert.Equal(string.Empty, missing.Header);
        Assert.False(unknown.IsAvailable);
        Assert.Equal("Nobody 3", unknown.Header);
        Assert.Equal("No class average is available for Nobody.", unknown.Status);
    }

    [Theory]
    [InlineData(CharacterStat.Hp, "HP")]
    [InlineData(CharacterStat.Sp, "SP")]
    [InlineData(CharacterStat.Attack, "ATT")]
    [InlineData(CharacterStat.Magic, "MAG")]
    [InlineData(CharacterStat.Skill, "SKL")]
    [InlineData(CharacterStat.Speed, "SPD")]
    [InlineData(CharacterStat.Luck, "LCK")]
    [InlineData(CharacterStat.Defense, "DEF")]
    [InlineData(CharacterStat.Resistance, "RES")]
    public void StatLabelsMatchTheStatsTab(CharacterStat stat, string expected) =>
        Assert.Equal(expected, CharacterStatLabels.Short(stat));
}
