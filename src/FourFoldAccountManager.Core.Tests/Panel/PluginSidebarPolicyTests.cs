using FourFoldAccountManager.Core.Panel;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Panel;

public sealed class PluginSidebarPolicyTests
{
    private static readonly Guid AccountId = Guid.NewGuid();

    [Fact]
    public void CollapsedSidebarNeverShows()
    {
        Assert.False(PluginSidebarPolicy.ShouldShow(isFullScreen: false, expanded: false, AccountId, [AccountId]));
        Assert.False(PluginSidebarPolicy.ShouldShow(isFullScreen: false, expanded: false, null, [AccountId]));
    }

    [Fact]
    public void ExpandedSidebarFollowsTheExistingRules()
    {
        Assert.True(PluginSidebarPolicy.ShouldShow(isFullScreen: false, expanded: true, AccountId, []));
        Assert.True(PluginSidebarPolicy.ShouldShow(isFullScreen: false, expanded: true, null, [AccountId]));
        Assert.False(PluginSidebarPolicy.ShouldShow(isFullScreen: false, expanded: true, null, []));
        Assert.False(PluginSidebarPolicy.ShouldShow(isFullScreen: true, expanded: true, AccountId, [AccountId]));
    }
}
