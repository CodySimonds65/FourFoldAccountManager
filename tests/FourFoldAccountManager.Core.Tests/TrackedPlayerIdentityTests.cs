using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Tests;

public class TrackedPlayerIdentityTests
{
    [Fact]
    public void Automatic_resolution_does_not_change_configured_identity()
    {
        var identity = new TrackedPlayerIdentity("Desmond", null);
        identity.ResolvedPlayerId = 83;
        Assert.True(identity.MatchesConfiguration("desmond", null));
        Assert.False(identity.MatchesConfiguration("Desmond", 83));
        Assert.False(identity.MatchesConfiguration("Other", null));
    }
}
