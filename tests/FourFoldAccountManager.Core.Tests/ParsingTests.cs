using System.IO;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Tests;

public class ParsingTests
{
    [Fact]
    public void Ranking_rows_expose_public_name_and_player_id()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ranking-total-exp.html"));
        var rows = RankingHtmlParser.Parse(html);
        Assert.Contains(new RankingEntry("Desmond", 83), rows);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Ranking_rejects_nonpositive_player_id()
    {
        const string html = "<table><tbody><tr><td>1</td><td><a href='player.php?id=0'>Desmond</a></td></tr></tbody></table>";
        Assert.Throws<InvalidDataException>(() => RankingHtmlParser.Parse(html));
    }

    [Fact]
    public void Profile_reads_classes_active_class_and_xp_remaining()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "player-profile.html"));
        var profile = PlayerProfileHtmlParser.Parse(html);
        Assert.Equal("Desmond", profile.Username);
        Assert.Equal("Evergreen Soldier", profile.ActiveClassName);
        Assert.Equal(10, profile.Classes[profile.ActiveClassName!].NextLevelXp - profile.Classes[profile.ActiveClassName!].CurrentXp);
        Assert.Equal(2, profile.Classes.Count);
        Assert.Equal(29728, profile.Classes["Sun Witch"].CurrentXp);
    }

    [Fact]
    public void Profile_without_active_badge_does_not_guess_active_class()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "player-profile.html"))
            .Replace("<div class=\"badge\">Active Class</div>", "");
        Assert.Null(PlayerProfileHtmlParser.Parse(html).ActiveClassName);
    }

    [Fact]
    public void Profile_keeps_healthy_class_when_other_card_is_incomplete()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "player-profile.html"))
            .Replace("<strong>EXP</strong>29,728 / 36,550", "<strong>EXP</strong>");
        var profile = PlayerProfileHtmlParser.Parse(html);
        Assert.Single(profile.Classes);
        Assert.Contains("Sun Witch", profile.InvalidClasses);
    }

    [Fact]
    public void Profile_requires_identity_and_classes()
    {
        Assert.Throws<InvalidDataException>(() => PlayerProfileHtmlParser.Parse("<div class='class-grid'></div>"));
    }
}
