using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Plugins;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Plugins;

public sealed class PluginLayoutPolicyTests
{
    [Fact]
    public void MovingAPluginKeepsTheSlotsOfUnloadedAndSwitchedOffPlugins()
    {
        // "missing" is saved but not loaded right now, and "off" is loaded but switched off; both must stay where they
        // are, or the user's saved order is silently rewritten.
        var settings = PanelSettings.Default with
        {
            PluginOrder = ["a", "missing", "off", "b"],
            DisabledPlugins = ["off"]
        };
        PluginDescriptor[] known = [Descriptor("a"), Descriptor("off"), Descriptor("b")];

        var moved = PluginLayoutPolicy.WithMoved(settings, known, "b", 0);

        Assert.Equal(["b", "missing", "off", "a"], moved.PluginOrder);
    }

    private static PluginDescriptor Descriptor(string id) => new(id, id, id, "", [], []);
}
