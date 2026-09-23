using FourFoldAccountManager.Core.Tracking;
using System.Text.Json;
using Xunit;

namespace FourFoldAccountManager.Core.Tests;

public sealed class PlayerProfileHtmlParserTests
{
    [Fact]
    public void Parse_reads_full_base_stats_and_equipment_for_each_class()
    {
        var snapshot = PlayerProfileHtmlParser.Parse(ReadFixture("player-profile-sample.html"));

        Assert.Equal("Dweebstify", snapshot.Username);
        Assert.Equal("Arctic Soldier", snapshot.ActiveClassName);

        var active = snapshot.Classes["Arctic Soldier"];
        Assert.Equal(278, active.Level);
        Assert.Equal(270_060L, active.CurrentXp);
        Assert.Equal(387_810L, active.NextLevelXp);
        Assert.Equal(22_130L, active.Hp);
        Assert.Equal(998L, active.Sp);
        Assert.Equal(238L, active.Attack);
        Assert.Equal(61L, active.Magic);
        Assert.Equal(179L, active.Skill);
        Assert.Equal(75L, active.Speed);
        Assert.Equal(216L, active.Defense);
        Assert.Equal(30L, active.Resistance);
        Assert.Equal(75L, active.Luck);
        Assert.Equal("Enhanced Arcticsoldier", active.Equipment["Armor"]);

        Assert.Equal(1, snapshot.Classes["Charmer"].Level);
        Assert.Empty(snapshot.InvalidStatClasses);
    }

    [Fact]
    public void Parse_uses_maximum_values_for_hp_and_sp()
    {
        var html = ReadFixture("player-profile-sample.html")
            .Replace("22,130 / 22,130", "1,234 / 2,345", StringComparison.Ordinal)
            .Replace("998 / 998", "100 / 250", StringComparison.Ordinal);

        var active = PlayerProfileHtmlParser.Parse(html).Classes["Arctic Soldier"];

        Assert.Equal(2_345L, active.Hp);
        Assert.Equal(250L, active.Sp);
    }

    [Fact]
    public void Parse_keeps_classes_with_missing_equipment_usable()
    {
        var charmer = PlayerProfileHtmlParser.Parse(ReadFixture("player-profile-sample.html")).Classes["Charmer"];

        Assert.Empty(charmer.Equipment);
    }

    [Fact]
    public void Parse_marks_malformed_required_stats_without_poisoning_xp_classes()
    {
        var html = ReadFixture("player-profile-sample.html")
            .Replace("<strong>HP</strong> 22,130 / 22,130", "<strong>HP</strong> unknown / 22,130", StringComparison.Ordinal);

        var snapshot = PlayerProfileHtmlParser.Parse(html);

        Assert.Contains("Arctic Soldier", snapshot.InvalidStatClasses);
        Assert.DoesNotContain("Arctic Soldier", snapshot.InvalidClasses);
        Assert.Null(snapshot.Classes["Arctic Soldier"].Hp);
    }

    [Fact]
    public void Parse_rejects_duplicate_classes()
    {
        var html = ReadFixture("player-profile-sample.html")
            .Replace("<h3>Charmer</h3>", "<h3>Arctic Soldier</h3>", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => PlayerProfileHtmlParser.Parse(html));
    }

    [Fact]
    public void Parse_leaves_active_class_unset_when_badge_is_missing()
    {
        var html = ReadFixture("player-profile-sample.html")
            .Replace("<div class=\"badge\">Active Class</div>", string.Empty, StringComparison.Ordinal);

        Assert.Null(PlayerProfileHtmlParser.Parse(html).ActiveClassName);
    }

    [Fact]
    public void Parse_keeps_invalid_xp_out_of_classes()
    {
        var html = ReadFixture("player-profile-sample.html")
            .Replace("270,060 / 387,810", "387,810 / 387,810", StringComparison.Ordinal);

        var snapshot = PlayerProfileHtmlParser.Parse(html);

        Assert.Contains("Arctic Soldier", snapshot.InvalidClasses);
        Assert.DoesNotContain("Arctic Soldier", snapshot.Classes.Keys);
    }

    [Fact]
    public void Legacy_snapshot_json_deserializes_with_nullable_profile_stats()
    {
        const string json = """
            {
              "username": "Dweebstify",
              "activeClassName": "Arctic Soldier",
              "classes": {
                "Arctic Soldier": {
                  "level": 278,
                  "currentXp": 270060,
                  "nextLevelXp": 387810,
                  "sourceUpdated": "Sep 21, 2026"
                }
              },
              "invalidClasses": []
            }
            """;

        var snapshot = JsonSerializer.Deserialize<PlayerProgressSnapshot>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.Classes["Arctic Soldier"].Hp);
        Assert.Empty(snapshot.InvalidStatClasses);
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
