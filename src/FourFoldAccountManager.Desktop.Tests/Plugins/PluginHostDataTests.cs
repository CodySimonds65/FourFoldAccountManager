using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Plugins;

public sealed class PluginHostDataTests
{
    [Fact]
    public void ABlankRankingNameNeverExposesTheProfileName()
    {
        // With no ranking name the profile was looked up by the saved login, so its name is the login.
        Assert.Null(MainWindow.PublicInGameName(null, "MyLogin"));
        Assert.Null(MainWindow.PublicInGameName("", "MyLogin"));
        Assert.Null(MainWindow.PublicInGameName("  ", "MyLogin"));
        Assert.Equal("HERO", MainWindow.PublicInGameName("Hero", "HERO"));
        Assert.Equal("Hero", MainWindow.PublicInGameName("Hero", null));
        // Just after a ranking name is saved, the last profile read is still the login's.
        Assert.Equal("Hero", MainWindow.PublicInGameName("Hero", "MyLogin"));
    }

    [Fact]
    public void ThePlayerIdIsOnlyGivenForAProfileReadUnderTheRankingName()
    {
        // The id leads to a public page that shows the profile's name, and with no ranking name that name is the login.
        Assert.Null(MainWindow.PublicPlayerId(null, "MyLogin", 277));
        Assert.Null(MainWindow.PublicPlayerId("", "MyLogin", 277));
        Assert.Null(MainWindow.PublicPlayerId("  ", "MyLogin", 277));
        // Just after a ranking name is saved, the tracker still holds the id it resolved for the login.
        Assert.Null(MainWindow.PublicPlayerId("Hero", "MyLogin", 277));
        Assert.Null(MainWindow.PublicPlayerId("Hero", null, 277));
        Assert.Equal(277, MainWindow.PublicPlayerId("Hero", "HERO", 277));
        Assert.Equal(277, MainWindow.PublicPlayerId(" Hero ", "Hero", 277));
        Assert.Null(MainWindow.PublicPlayerId("Hero", "Hero", null));
    }
}
