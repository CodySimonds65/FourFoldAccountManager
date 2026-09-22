using FourFoldAccountManager.Core.Panel;

namespace FourFoldAccountManager.Core.Tests;

public class TrackerPanelPolicyTests
{
    [Fact]
    public void Fullscreen_never_displays_tracker()
    {
        Assert.False(TrackerPanelPolicy.ShouldShow(true, [Guid.NewGuid()]));
    }

    [Fact]
    public void Normal_mode_displays_tracker_only_with_open_clients()
    {
        Assert.False(TrackerPanelPolicy.ShouldShow(false, []));
        Assert.True(TrackerPanelPolicy.ShouldShow(false, [Guid.NewGuid()]));
    }
}
