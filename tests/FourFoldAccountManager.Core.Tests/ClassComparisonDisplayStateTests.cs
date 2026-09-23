using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Tracking;
using Xunit;

namespace FourFoldAccountManager.Core.Tests;

public sealed class ClassComparisonDisplayStateTests
{
    [Fact]
    public void FromSnapshot_maps_active_class_rows_and_timestamp_in_order()
    {
        var active = new ClassProfileSnapshot(278, 270_060, 387_810, "Sep 21, 2026")
        {
            ClassName = "Arctic Soldier",
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
        var snapshot = new PlayerProgressSnapshot("Dweebstify", "Arctic Soldier",
            new Dictionary<string, ClassProfileSnapshot>
            {
                ["Arctic Soldier"] = active
            }, []);

        var state = ClassComparisonDisplayState.FromSnapshot(snapshot);

        Assert.True(state.IsAvailable);
        Assert.Equal("Arctic Soldier", state.ActiveClassName);
        Assert.Equal(278, state.Level);
        Assert.Equal("Sep 21, 2026", state.SourceUpdated);
        Assert.Equal(9, state.Rows.Count);
        Assert.Equal(CharacterStat.Hp, state.Rows[0].Stat);
        Assert.Equal(ComparisonDirection.Above, state.Rows[0].Direction);
        Assert.Equal(CharacterStat.Resistance, state.Rows[^1].Stat);
    }

    [Fact]
    public void FromSnapshot_returns_unavailable_when_active_class_is_invalid()
    {
        var snapshot = new PlayerProgressSnapshot("Dweebstify", "Missing Class",
            new Dictionary<string, ClassProfileSnapshot>(), []);

        var state = ClassComparisonDisplayState.FromSnapshot(snapshot);

        Assert.False(state.IsAvailable);
        Assert.Empty(state.Rows);
        Assert.Contains("active class", state.Status, StringComparison.OrdinalIgnoreCase);
    }
}
