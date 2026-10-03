using System.Windows;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Desktop.Views;

namespace FourFoldAccountManager.Desktop.Plugins;

public sealed class XpCalcPlugin : IFourFoldPlugin
{
    public XpCalcPlugin()
    {
        View.RefreshRequested += (_, _) => RefreshRequested?.Invoke();
    }

    // Asks for a fresh read of the selected account's profile.
    public event Action? RefreshRequested;

    public ExperienceCalculatorPanel View { get; } = new();

    public PluginDescriptor Descriptor => BuiltInPlugins.XpCalc;

    public FrameworkElement Panel => View;

    public FrameworkElement? SettingsPage => null;

    // Opening XP calc shows the selected account's current profile, as clicking its old button did.
    public void Opened() => RefreshRequested?.Invoke();
}
