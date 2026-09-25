using System.Windows;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class PluginSidebarTimerTests
{
    [Fact]
    public void ShowingTheTimerPluginShowsOnlyTheTimerTab() => WpfTestHost.Run(() =>
    {
        var sidebar = new PluginSidebar();

        sidebar.ShowPlugin(PluginKind.Timer);

        Assert.Equal(PluginKind.Timer, sidebar.ActivePlugin);
        Assert.Equal(Visibility.Visible, sidebar.TimerPanelView.Visibility);
        Assert.Equal(Visibility.Collapsed, sidebar.TrackerPanel.Visibility);
        Assert.Equal(Visibility.Collapsed, sidebar.ClassComparisonPanelView.Visibility);
        Assert.Equal(Visibility.Collapsed, sidebar.XpCalculatorPanelView.Visibility);
        Assert.Equal(1d, sidebar.TimerButton.Opacity);
        Assert.Equal(0.65d, sidebar.XpTrackerButton.Opacity);
    });
}
