using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Tracking;
using Xunit;

namespace FourFoldAccountManager.Core.Tests;

public sealed class ExperienceCalculatorStateTests
{
    [Fact]
    public void State_defaults_target_to_the_next_level_and_formats_level_25_to_33()
    {
        var state = ExperienceCalculatorState.FromProfile(Profile(25, 0));

        Assert.Equal(26, state.TargetLevel);
        var target = state.WithTarget(33);
        Assert.Equal("33,840", target.RemainingXpText);
        Assert.Equal("26,000", target.CurrentAbsoluteXpText);
        Assert.Equal("59,840", target.TargetAbsoluteXpText);
        Assert.Equal(8, target.LevelsRemaining);
    }

    [Fact]
    public void State_subtracts_current_progress_and_handles_same_or_lower_targets()
    {
        var state = ExperienceCalculatorState.FromProfile(Profile(25, 1_000));

        Assert.Equal("32,840", state.WithTarget(33).RemainingXpText);
        Assert.Equal("0", state.WithTarget(25).RemainingXpText);
        Assert.False(state.WithTarget(24).IsValid);
        Assert.Contains("below", state.WithTarget(24).Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void State_labels_invalid_progress_as_level_start()
    {
        var state = ExperienceCalculatorState.FromProfile(Profile(25, 3_250)).WithTarget(33);

        Assert.True(state.IsValid);
        Assert.False(state.UsedCurrentProgress);
        Assert.Contains("level start", state.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("33,840", state.RemainingXpText);
    }

    [Fact]
    public void State_selects_the_active_class_from_a_profile_snapshot()
    {
        var snapshot = new PlayerProgressSnapshot("Dweebstify", "Arctic Soldier",
            new Dictionary<string, ClassProfileSnapshot>
            {
                ["Arctic Soldier"] = Profile(25, 0)
            }, []);

        var state = ExperienceCalculatorState.FromSnapshot(snapshot);

        Assert.True(state.IsValid);
        Assert.Equal("Arctic Soldier", state.ClassName);
    }

    private static ClassProfileSnapshot Profile(int level, long currentXp) =>
        new(level, currentXp, 5L * level * (level + 1), "today") { ClassName = "Arctic Soldier" };
}
