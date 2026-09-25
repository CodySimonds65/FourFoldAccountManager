using FourFoldAccountManager.Core.Tracking;
using FourFoldAccountManager.Leaderboard.Service.Data;
using Xunit;

namespace FourFoldAccountManager.Leaderboard.Tests.Data;

public sealed class XpSnapshotJsonTests
{
    [Fact]
    public void StoresOnlyXpFieldsAndPreservesCalculatorInputs()
    {
        var before = new PlayerProgressSnapshot("Alice", "Mage",
            new Dictionary<string, ClassProfileSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mage"] = new(1, 2, 10, "source") { Hp = 900, Equipment = new Dictionary<string, string> { ["Weapon"] = "Secret" } }
            }, ["Knight"]);
        var json = XpSnapshotJson.Serialize(before);
        Assert.DoesNotContain("Hp", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Weapon", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source", json, StringComparison.OrdinalIgnoreCase);
        var restored = XpSnapshotJson.Deserialize(json, "Alice");
        Assert.Equal(before.Classes["Mage"].CurrentXp, restored.Classes["mage"].CurrentXp);
        Assert.Equal(["Knight"], restored.InvalidClasses);
        var after = new PlayerProgressSnapshot("Alice", "Mage",
            new Dictionary<string, ClassProfileSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mage"] = new(1, 7, 10, null)
            }, ["Knight"]);
        Assert.Equal(XpProgressCalculator.Calculate(before, after).ValidGain,
            XpProgressCalculator.Calculate(restored, XpSnapshotJson.Deserialize(XpSnapshotJson.Serialize(after), "Alice")).ValidGain);
    }
}
