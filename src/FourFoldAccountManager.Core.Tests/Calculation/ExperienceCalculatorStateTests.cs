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
}
