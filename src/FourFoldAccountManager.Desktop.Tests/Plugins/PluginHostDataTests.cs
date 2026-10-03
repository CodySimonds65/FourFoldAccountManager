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
    }
}
