using System.Windows;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Desktop.Views;

namespace FourFoldAccountManager.Desktop.Plugins;

public sealed class StatsPlugin : IFourFoldPlugin
{
    public StatsPlugin()
    {
        View.RefreshRequested += (_, _) => RefreshRequested?.Invoke();
    }

    // Asks for a fresh read of the selected account's profile.
    public event Action? RefreshRequested;

    public ClassComparisonPanel View { get; } = new();

    public PluginDescriptor Descriptor => BuiltInPlugins.Stats;

    public FrameworkElement Panel => View;

    public FrameworkElement? SettingsPage => null;

    // Opening Stats shows the selected account's current profile, as clicking its old button did.
    public void Opened() => RefreshRequested?.Invoke();
}
