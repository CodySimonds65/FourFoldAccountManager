using FourFoldAccountManager.Core.Tracking;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Tracking;

public sealed class PlayerProfileHtmlParserTests
{
    // The shape of social_profile.php: the game-stats strip sits above the class cards.
    private static string Page(string gameStats) => $$"""
        <div class="social-profile-identity"><h1><span class="social-name">Alice</span></h1></div>
        {{gameStats}}
        <div class="social-class-panels"><article class="social-loadout-card">
        <div class="social-loadout-head"><div><span>Active class</span><h3>Warrior</h3><strong>Level 3</strong></div></div>
        <div class="social-loadout-vitals"><span>EXP <strong>5 / 10</strong></span></div>
        </article></div>
        """;

    private static string Strip(string silver) => $$"""
        <div class="social-game-stats">
        <div class="social-game-stat"><span>Gold</span><strong>95,050</strong></div>
        <div class="social-game-stat"><span>Silver</span>{{silver}}</div>
        <div class="social-game-stat"><span>Location</span><strong> Arena </strong></div>
        </div>
        """;

    [Fact]
    public void AChangedGameStatsStripNeverBreaksTheClasses()
    {
        // XP tracking and the leaderboard don't need the strip. Without it, or with a silver value that isn't a
        // number, the classes still parse and silver is simply unknown.
        foreach (var strip in new[] { "", Strip("<strong>lots</strong>"), Strip("<strong>-5</strong>"), Strip("") })
        {
            var snapshot = PlayerProfileHtmlParser.Parse(Page(strip));

            Assert.Null(snapshot.Silver);
            Assert.Equal("Warrior", snapshot.ActiveClassName);
            Assert.Equal(5, snapshot.Classes["Warrior"].CurrentXp);
        }

        var read = PlayerProfileHtmlParser.Parse(Page(Strip("<strong>13,025,343</strong>")));

        Assert.Equal(13_025_343, read.Silver);
        Assert.Equal(95_050, read.Gold);
        Assert.Equal("Arena", read.Location);
    }
}
