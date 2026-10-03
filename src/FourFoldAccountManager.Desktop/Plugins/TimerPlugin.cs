using System.Windows;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Desktop.Services;
using FourFoldAccountManager.Desktop.Views;

namespace FourFoldAccountManager.Desktop.Plugins;

public sealed class TimerPlugin : IFourFoldPlugin
{
    private const string UnavailableStatus =
        "Unavailable — another app or another FourFold shortcut may be using these keys. Choose a different combination.";

    private readonly TimerSettingsPage _settingsPage = new();

    public TimerPlugin(TimerCoordinator coordinator)
    {
        View.Attach(coordinator);
        _settingsPage.ChangeRequested += (action, chord) => ShortcutChangeRequested?.Invoke(action, chord);
    }

    public event Action<GlobalShortcutAction, GlobalHotkeyChord>? ShortcutChangeRequested;

    public TimerPanel View { get; } = new();

    public PluginDescriptor Descriptor => BuiltInPlugins.Timer;

    public FrameworkElement Panel => View;

    public FrameworkElement? SettingsPage => _settingsPage;

    // While true, MainWindow must not act on shortcut keys: the user is pressing keys to bind them.
    public bool IsCapturingShortcut => _settingsPage.IsCapturing;

    public void Opened()
    {
    }

    public void ShowShortcuts(PanelSettings settings, Func<GlobalShortcutAction, bool> isAvailable)
    {
        View.SetHotkeys(
            ShortcutText.Format(settings.TimerSplitShortcut),
            ShortcutText.Format(settings.TimerFinishShortcut),
            ShortcutText.Format(settings.TimerResetShortcut),
            Descriptor.Shortcuts.Any(action => !isAvailable(action)));
        var enabled = PluginLayoutPolicy.IsEnabled(settings, Descriptor.Id);
        foreach (var action in Descriptor.Shortcuts)
        {
            _settingsPage.ShowKeys(action,
                ShortcutText.Format(GlobalShortcutActions.GetChord(settings, action)),
                enabled && !isAvailable(action) ? UnavailableStatus : string.Empty);
        }
    }

    public void ShowShortcutStatus(GlobalShortcutAction action, string status) =>
        _settingsPage.ShowStatus(action, status);
}
