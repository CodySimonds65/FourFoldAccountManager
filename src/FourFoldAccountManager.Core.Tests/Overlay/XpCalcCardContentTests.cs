using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Tracking;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Overlay;

public sealed class XpCalcCardContentTests
{
    [Fact]
    public void NoSnapshotWaitsForTheProfile()
    {
        var content = XpCalcCardContent.FromSnapshot(null, savedTargetLevel: 50);

        Assert.False(content.IsAvailable);
        Assert.Equal("Waiting for profile…", content.Status);
        Assert.Null(content.TargetLevel);
    }

    [Fact]
    public void WithoutASavedTargetTheCardAimsAtTheNextLevel()
    {
        var content = XpCalcCardContent.FromSnapshot(Snapshot(), savedTargetLevel: null);

        Assert.True(content.IsAvailable);
        Assert.Equal("Warrior 2 → 3", content.TargetLine);
        Assert.Equal("25 XP · 1 level to go", content.RemainingLine);
        Assert.Equal(3, content.TargetLevel);
    }

    [Fact]
    public void SavedTargetAboveTheCurrentLevelIsUsed()
    {
        var snapshot = Snapshot();
        var expectedXp = ExperienceCalculatorState.FromSnapshot(snapshot).WithTarget(5).RemainingXpText;

        var content = XpCalcCardContent.FromSnapshot(snapshot, savedTargetLevel: 5);

        Assert.Equal("Warrior 2 → 5", content.TargetLine);
        Assert.Equal($"{expectedXp} XP · 3 levels to go", content.RemainingLine);
        Assert.Equal(5, content.TargetLevel);
    }

    [Theory]
    [InlineData(2L)]
    [InlineData(1L)]
    public void SavedTargetAtOrBelowTheCurrentLevelIsReached(long savedTarget)
    {
        var content = XpCalcCardContent.FromSnapshot(Snapshot(), savedTarget);

        Assert.True(content.IsAvailable);
        Assert.Equal($"Warrior 2 → {savedTarget}", content.TargetLine);
        Assert.Equal("Target reached", content.RemainingLine);
    }

    [Fact]
    public void MissingActiveClassShowsTheCalculatorStatus()
    {
        var content = XpCalcCardContent.FromSnapshot(
            new PlayerProgressSnapshot("Alice", null, new Dictionary<string, ClassProfileSnapshot>(), []), 50);

        Assert.False(content.IsAvailable);
        Assert.Equal("The active class is unavailable.", content.Status);
    }

    private static PlayerProgressSnapshot Snapshot() =>
        new("Alice", "Warrior", new Dictionary<string, ClassProfileSnapshot>
        {
            ["Warrior"] = new(2, 5, 30, null) { ClassName = "Warrior" }
        }, []);
}
