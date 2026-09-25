using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Tracking;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Calculation;

public sealed class ExperienceCalculatorStateTests
{
    private static readonly ClassProfileSnapshot Warrior = new(2, 5, 30, null)
    {
        ClassName = "Warrior"
    };

    [Fact]
    public void LoadedProfileWaitsForManualTarget()
    {
        var state = ExperienceCalculatorState.FromProfile(Warrior);

        Assert.Equal("Warrior", state.ClassName);
        Assert.Equal(2, state.CurrentLevel);
        Assert.Equal(0, state.TargetLevel);
        Assert.Null(state.Projection);
        Assert.Equal("—", state.RemainingXpText);
        Assert.Equal("—", state.TargetAbsoluteXpText);
        Assert.Empty(state.Projection?.Transitions ?? []);
    }

    [Fact]
    public void EnteringAndClearingTargetUpdatesProjection()
    {
        var selected = ExperienceCalculatorState.FromProfile(Warrior).WithTarget(3);

        Assert.Equal("25", selected.RemainingXpText);
        Assert.NotNull(selected.Projection);

        var cleared = selected.WithoutTarget();
        Assert.Equal(0, cleared.TargetLevel);
        Assert.Null(cleared.Projection);
        Assert.Equal("—", cleared.RemainingXpText);
    }

    [Fact]
    public void TargetAboveTheCapIsInvalidWithoutProjecting()
    {
        var state = ExperienceCalculatorState.FromProfile(Warrior).WithTarget(XpCalculatorTargets.MaxTargetLevel + 1);

        Assert.False(state.IsValid);
        Assert.Equal("Target level must be 9,999 or lower.", state.Status);
        Assert.Equal("—", state.RemainingXpText);
        Assert.Equal("—", state.CurrentAbsoluteXpText);
        Assert.Equal("—", state.TargetAbsoluteXpText);
        Assert.Equal(0, state.LevelsRemaining);
        Assert.Null(state.Projection);
    }

    [Fact]
    public void SnapshotWithActiveClassDoesNotChooseNextLevel()
    {
        var snapshot = new PlayerProgressSnapshot("Alice", "Warrior",
            new Dictionary<string, ClassProfileSnapshot> { ["Warrior"] = Warrior }, []);

        Assert.Equal(0, ExperienceCalculatorState.FromSnapshot(snapshot).TargetLevel);
        Assert.Null(ExperienceCalculatorState.FromSnapshot(snapshot with { ActiveClassName = null }).Projection);
    }
}
