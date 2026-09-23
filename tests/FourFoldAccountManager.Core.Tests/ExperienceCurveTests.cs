using System.Numerics;
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Tracking;
using Xunit;

namespace FourFoldAccountManager.Core.Tests;

public sealed class ExperienceCurveTests
{
    [Fact]
    public void Total_xp_matches_the_published_curve()
    {
        Assert.Equal(BigInteger.Zero, ExperienceCurve.TotalXpAtLevel(1));
        Assert.Equal(new BigInteger(10), ExperienceCurve.TotalXpAtLevel(2));
        Assert.Equal(new BigInteger(26_000), ExperienceCurve.TotalXpAtLevel(25));
        Assert.Equal(new BigInteger(59_840), ExperienceCurve.TotalXpAtLevel(33));
        Assert.Equal(new BigInteger(1_666_500), ExperienceCurve.TotalXpAtLevel(100));
        Assert.Equal(new BigInteger(33_840), ExperienceCurve.XpBetweenLevels(25, 33));
    }

    [Fact]
    public void Project_subtracts_current_progress_and_builds_transitions()
    {
        var profile = new ClassProfileSnapshot(25, 1_000, 3_250, "today") { ClassName = "Test" };

        var result = ExperienceCurve.Project(profile, 33);

        Assert.True(result.IsValid);
        Assert.Equal(new BigInteger(27_000), result.CurrentAbsoluteXp);
        Assert.Equal(new BigInteger(59_840), result.TargetAbsoluteXp);
        Assert.Equal(new BigInteger(32_840), result.RemainingXp);
        Assert.True(result.UsedCurrentProgress);
        Assert.Equal(8, result.Transitions.Count);
    }

    [Fact]
    public void Project_handles_same_level_lower_target_and_invalid_progress()
    {
        var profile = new ClassProfileSnapshot(25, 1_000, 3_250, "today");
        var same = ExperienceCurve.Project(profile, 25);
        var lower = ExperienceCurve.Project(profile, 24);
        var invalid = ExperienceCurve.Project(profile with { CurrentXp = 3_250 }, 33);

        Assert.True(same.IsValid);
        Assert.Equal(BigInteger.Zero, same.RemainingXp);
        Assert.False(lower.IsValid);
        Assert.False(invalid.UsedCurrentProgress);
        Assert.Equal(new BigInteger(33_840), invalid.RemainingXp);
    }

    [Fact]
    public void Project_uses_integer_safe_arithmetic_for_large_targets()
    {
        var profile = new ClassProfileSnapshot(25, 0, 3_250, "today");

        var result = ExperienceCurve.Project(profile, 10_000);

        Assert.True(result.IsValid);
        Assert.True(ExperienceCurve.TotalXpAtLevel(2_000_000) > long.MaxValue);
    }
}
