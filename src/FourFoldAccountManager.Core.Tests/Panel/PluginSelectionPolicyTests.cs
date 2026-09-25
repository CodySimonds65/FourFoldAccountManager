using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Panel;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Panel;

public sealed class PluginSelectionPolicyTests
{
    [Theory]
    [InlineData(PluginKind.XpTracker)]
    [InlineData(PluginKind.ClassComparison)]
    [InlineData(PluginKind.XpCalculator)]
    [InlineData(PluginKind.Timer)]
    public void KnownPluginsAreKept(PluginKind plugin) =>
        Assert.Equal(plugin, PluginSelectionPolicy.Normalize(plugin));

    [Fact]
    public void MissingOrUnknownPluginsFallBackToTheTracker()
    {
        Assert.Equal(PluginKind.XpTracker, PluginSelectionPolicy.Normalize(null));
        Assert.Equal(PluginKind.XpTracker, PluginSelectionPolicy.Normalize((PluginKind)99));
    }
}
