using System.Windows;
using FourFoldAccountManager.Core.Plugins;

namespace FourFoldAccountManager.Desktop.Plugins;

// A plugin the sidebar hosts: built-in WPF panels today, sandboxed web plugins later.
public interface IFourFoldPlugin
{
    PluginDescriptor Descriptor { get; }

    // Shown beside the strip while the plugin is open.
    FrameworkElement Panel { get; }

    // Opened from the plugin list's cog; null means the plugin has no settings and no cog.
    FrameworkElement? SettingsPage { get; }

    // Called each time the panel starts showing this plugin.
    void Opened();
}
