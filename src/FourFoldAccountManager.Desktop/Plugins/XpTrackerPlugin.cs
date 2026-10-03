using System.Windows;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Desktop.Views;

namespace FourFoldAccountManager.Desktop.Plugins;

public sealed class XpTrackerPlugin : IFourFoldPlugin
{
    public XpTrackerPanel View { get; } = new();

    public PluginDescriptor Descriptor => BuiltInPlugins.XpTracker;

    public FrameworkElement Panel => View;

    public FrameworkElement? SettingsPage => null;

    public void Opened()
    {
    }
}
