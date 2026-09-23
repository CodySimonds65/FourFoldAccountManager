using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Tracking;
using Xunit;

namespace FourFoldAccountManager.Core.Tests;

public sealed class ClassComparisonCalculatorTests
{
    [Fact]
    public void Compare_uses_runtime_active_class_and_all_nine_stats()
    {
        var profile = Profile("Arctic Soldier", 278);

        var result = new ClassComparisonCalculator().Compare(profile);

        Assert.True(result.IsAvailable);
        Assert.Equal(9, result.Rows.Count);
        Assert.Equal(CharacterStat.Hp, result.Rows[0].Stat);
        Assert.Equal(22_102L, result.Rows[0].Average);
        Assert.Equal(28L, result.Rows[0].Difference);
        Assert.Equal(ComparisonDirection.Above, result.Rows[0].Direction);
        Assert.Equal(4, result.AboveCount);
        Assert.Equal(3, result.BelowCount);
        Assert.Equal(2, result.EqualCount);
        Assert.NotNull(result.MeanPercentageDifference);
    }

    [Fact]
    public void Compare_returns_unavailable_for_unknown_class_or_missing_stat()
    {
        var unknown = new ClassComparisonCalculator().Compare(Profile("Unknown", 25));
        var missing = Profile("Arctic Soldier", 25) with { Hp = null };

        Assert.False(unknown.IsAvailable);
        Assert.False(new ClassComparisonCalculator().Compare(missing).IsAvailable);
    }

    [Fact]
    public void Compare_returns_unavailable_for_zero_or_nonfinite_average()
    {
        var zero = CatalogWith("Zero", 0d);
        var nonfinite = CatalogWith("Nonfinite", double.NaN);

        Assert.False(new ClassComparisonCalculator(zero).Compare(Profile("Test", 1)).IsAvailable);
        Assert.False(new ClassComparisonCalculator(nonfinite).Compare(Profile("Test", 1)).IsAvailable);
    }

    private static ClassProfileSnapshot Profile(string className, int level) =>
        new(level, 1_000, 5L * level * (level + 1), "today")
        {
            ClassName = className,
            Hp = 22_130,
            Sp = 998,
            Attack = 238,
            Magic = 61,
            Skill = 179,
            Speed = 75,
            Defense = 216,
            Resistance = 30,
            Luck = 75
        };

    private static ClassAverageCatalog CatalogWith(string className, double hpAverage) =>
        new(new Dictionary<string, ClassAverageDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [className] = new(className, "Test",
                new Dictionary<CharacterStat, double>
                {
                    [CharacterStat.Hp] = hpAverage,
                    [CharacterStat.Sp] = 1,
                    [CharacterStat.Attack] = 1,
                    [CharacterStat.Magic] = 1,
                    [CharacterStat.Skill] = 1,
                    [CharacterStat.Speed] = 1,
                    [CharacterStat.Luck] = 1,
                    [CharacterStat.Defense] = 1,
                    [CharacterStat.Resistance] = 1
                },
                new Dictionary<CharacterStat, double>())
        });
}
