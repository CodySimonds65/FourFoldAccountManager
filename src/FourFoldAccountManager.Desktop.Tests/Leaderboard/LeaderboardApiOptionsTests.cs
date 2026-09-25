using FourFoldAccountManager.Desktop.Services;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Leaderboard;

public sealed class LeaderboardApiOptionsTests
{
    [Theory]
    [InlineData("http://leaderboard.example")]
    [InlineData("https://leaderboard.example/private")]
    [InlineData("https://user:password@leaderboard.example")]
    [InlineData("https://leaderboard.example/?token=secret")]
    public void RejectsUnsafeOrNonRootUrl(string value) =>
        Assert.Null(LeaderboardApiOptions.FromConfiguredUrl(value));
}
