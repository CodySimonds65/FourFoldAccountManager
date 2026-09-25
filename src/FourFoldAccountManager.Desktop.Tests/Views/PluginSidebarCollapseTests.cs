using System.Windows;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class PluginSidebarCollapseTests
{
    [Fact]
    public void CollapsedSidebarStaysHiddenWhileAnAccountIsOpen() => WpfTestHost.Run(() =>
    {
        var sidebar = new PluginSidebar();
        var openAccounts = new[] { Guid.NewGuid() };

        Assert.False(sidebar.UpdateHostVisibility(workspaceVisible: true, isFullScreen: false, expanded: false, openAccounts));
        Assert.Equal(Visibility.Collapsed, sidebar.Visibility);

        Assert.True(sidebar.UpdateHostVisibility(workspaceVisible: true, isFullScreen: false, expanded: true, openAccounts));
        Assert.Equal(Visibility.Visible, sidebar.Visibility);
    });
}
