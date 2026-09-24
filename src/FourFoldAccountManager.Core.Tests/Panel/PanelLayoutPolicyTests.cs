using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Panel;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Panel;

public sealed class PanelLayoutPolicyTests
{
    [Fact]
    public void SettingsTransformsPreserveLeaderboardSharingPreference()
    {
        var accountId = Guid.NewGuid();
        var settings = PanelSettings.Default with
        {
            ShareLinkedAccounts = true,
            SlotAccountIds = [accountId, null, null, null, null]
        };

        Assert.True(PanelLayoutPolicy.WithLayout(settings, PanelLayout.OneByTwo).ShareLinkedAccounts);
        Assert.True(PanelLayoutPolicy.Assign(settings, 1, Guid.NewGuid()).ShareLinkedAccounts);
        Assert.True(PanelLayoutPolicy.ClearAccount(settings, accountId).ShareLinkedAccounts);
        Assert.True(PanelLayoutPolicy.WithSplitState(settings,
            new PanelSplitState("2x2.rows", [0.7, 0.3])).ShareLinkedAccounts);
        Assert.True(PanelLayoutPolicy.ResetSplitStates(settings).ShareLinkedAccounts);
    }
}
