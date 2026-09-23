using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Panel;
using Xunit;

namespace FourFoldAccountManager.Core.Tests;

public sealed class PluginSelectionPolicyTests
{
    [Fact]
    public void Normalize_defaults_to_xp_tracker_for_unknown_values()
    {
        Assert.Equal(PluginKind.XpTracker, PluginSelectionPolicy.Normalize(null));
        Assert.Equal(PluginKind.XpTracker, PluginSelectionPolicy.Normalize((PluginKind)999));
    }

    [Fact]
    public void Normalize_preserves_each_known_plugin()
    {
        Assert.Equal(PluginKind.XpTracker, PluginSelectionPolicy.Normalize(PluginKind.XpTracker));
        Assert.Equal(PluginKind.ClassComparison, PluginSelectionPolicy.Normalize(PluginKind.ClassComparison));
        Assert.Equal(PluginKind.XpCalculator, PluginSelectionPolicy.Normalize(PluginKind.XpCalculator));
    }
}
