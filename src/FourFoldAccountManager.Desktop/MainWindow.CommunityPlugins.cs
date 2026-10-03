using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Core.Plugins.Hub;
using FourFoldAccountManager.Core.Timing;
using FourFoldAccountManager.Desktop.Plugins;
using FourFoldAccountManager.Desktop.Plugins.Hub;
using FourFoldAccountManager.Desktop.Plugins.Web;
using FourFoldAccountManager.Desktop.Views;

namespace FourFoldAccountManager.Desktop;

// Community plugins: loading, the data they may read, and the events they are sent.
public partial class MainWindow : IPluginHostData
{
    private readonly PluginCardStore _pluginCards = new();
    private readonly Dictionary<Guid, (DateTimeOffset? LastUpdated, bool IsStale, double? RatePerHour, long SessionGain)>
        _postedXpUpdates = [];
    private CommunityPluginManager _communityPlugins = null!;
    private PluginHub _pluginHub = null!;
    private string _postedAccounts = string.Empty;
    private System.Windows.Threading.DispatcherTimer? _pluginCardRefreshTimer;

    // Built-in plugins first, then community plugins: the one list the strip, the plugin list and the layout policy use.
    private IReadOnlyList<IFourFoldPlugin> AllPlugins => [.. _plugins.All, .. _communityPlugins.Plugins];

    private IReadOnlyList<PluginDescriptor> AllPluginDescriptors =>
        AllPlugins.Select(plugin => plugin.Descriptor).ToArray();

    private void InitializeCommunityPlugins(LocalDataPaths paths)
    {
        _communityPlugins = new CommunityPluginManager(paths, this, PluginParkingHost, _pluginCards);
        _communityPlugins.Changed += () =>
        {
            RefreshPluginSidebar();
            RefreshTrackerRows();
        };
        _pluginHub = new PluginHub(paths, _communityPlugins);
        _pluginHub.Changed += () => PluginSidebar.SetHubState(_pluginHub.ViewState);
        // An uninstalled plugin's cards, its place in the strip and its switch go from the saved settings too.
        _pluginHub.Uninstalled += id =>
            _ = ApplyPluginChangeAsync(settings => HubPolicy.WithUninstalled(settings, id), refreshEffects: true);
        PluginSidebar.HubOpened += () => _ = _pluginHub.RefreshAsync(userAsked: false);
        PluginSidebar.HubRetryRequested += () => _ = _pluginHub.RefreshAsync(userAsked: true);
        PluginSidebar.HubInstallRequested += id => _ = _pluginHub.InstallAsync(id);
        PluginSidebar.HubUninstallRequested += ConfirmPluginUninstall;
        // The catalog reader only lets through https://github.com/<owner>/<repo>, and OpenInBrowser opens https only.
        PluginSidebar.HubSourceRequested += uri => ((IPluginHostData)this).OpenInBrowser(uri);
        _pluginCards.Changed += QueuePluginCardRefresh;
        _timer.StateChanged += () => _communityPlugins.PostEvent("timer.changed", null);
        PluginSidebar.DeveloperModeChangeRequested += on =>
            _ = ApplyPluginChangeAsync(
                settings => settings.PluginDeveloperMode == on ? settings : settings with { PluginDeveloperMode = on },
                refreshEffects: true);
        PluginSidebar.PanelOpenRequested += () =>
            _ = ApplyPluginChangeAsync(
                settings => settings.PluginsSidebarExpanded ? settings : settings with { PluginsSidebarExpanded = true },
                refreshEffects: false);
        PluginSidebar.OpenDevFolderRequested += () =>
        {
            try
            {
                Directory.CreateDirectory(paths.DevPluginsRoot);
                Process.Start(new ProcessStartInfo(paths.DevPluginsRoot) { UseShellExecute = true });
            }
            catch (Exception)
            {
                GlobalStatusText.Text = "The dev plugins folder couldn't be opened.";
            }
        };
    }

    // Uninstalling deletes the plugin's saved data, so it asks first, and No is the answer Enter gives. The name comes from
    // the catalog or the plugin, so it only ever goes into the message as it is.
    private void ConfirmPluginUninstall(string id)
    {
        var state = _pluginHub.ViewState;
        var name = state.Plugins.FirstOrDefault(plugin => plugin.Id == id)?.Name
                   ?? state.Pulled.FirstOrDefault(plugin => plugin.Id == id)?.Name
                   ?? id;
        var answer = MessageBox.Show(
            this,
            $"Uninstall {name}? Its saved data on this computer will be deleted.",
            "Uninstall plugin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            _ = _pluginHub.UninstallAsync(id);
        }
    }

    private void RefreshPluginSidebar()
    {
        PluginSidebar.SetPlugins(AllPlugins);
        PluginSidebar.SetCommunityState(_communityPlugins.Rejected, _communityPlugins.StartupError);
    }

    // A plugin can set its cards many times a second; they are redrawn at most four times a second.
    private void QueuePluginCardRefresh()
    {
        if (_shutdownStarted)
        {
            return;
        }

        if (_pluginCardRefreshTimer is null)
        {
            _pluginCardRefreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _pluginCardRefreshTimer.Tick += (_, _) =>
            {
                // A click that straddles a rebuild of the switches is lost, so wait for the button to come up; the
                // timer keeps running and tries again 250 ms later.
                if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed)
                {
                    return;
                }

                _pluginCardRefreshTimer.Stop();
                RefreshTrackerRows();
            };
        }

        if (!_pluginCardRefreshTimer.IsEnabled)
        {
            _pluginCardRefreshTimer.Start();
        }
    }

    // Adds every switched-on community plugin's cards of this scope: global cards once, account cards per open account.
    private void AddPluginCards(
        OverlayAddOnScope scope,
        Guid? accountId,
        string accountLabel,
        bool showOverGame,
        List<OverlayTraySwitch> switches,
        OverlayCardBuild build)
    {
        foreach (var plugin in _communityPlugins.Plugins.Where(
                     plugin => PluginLayoutPolicy.IsEnabled(_panelSettings, plugin.Descriptor.Id)))
        {
            foreach (var card in plugin.Descriptor.Cards.Where(card => card.Scope == scope))
            {
                var key = new OverlayCardKey(
                    OverlayAddOnKind.Plugin, accountId, PluginCardId.Create(plugin.Descriptor.Id, card.Id));
                var content = _pluginCards.Get(key);
                var data = new PluginCardData(
                    card.Name,
                    accountLabel,
                    IsStale: !plugin.IsRunning,
                    content?.Rows.Select(row => new PluginCardRowData(row.Label, row.Value, row.Progress)).ToArray() ?? [],
                    content?.Summary ?? "No data yet");
                // The short label says whose card it is: two plugins can both declare "Goals", and a card named "Timer"
                // must not pass for FourFold's own in the Overlays panel and the floating checklist.
                AddOverlayCard(
                    new OverlayAddOnDefinition(
                        OverlayAddOnKind.Plugin, scope, $"{card.Name} ({plugin.Descriptor.ShortLabel})", 220, 120, 150, 44),
                    key, accountLabel, data, showOverGame, switches, build);
            }
        }
    }

    // Tells running plugins what changed since the last refresh: the account list, and anything in an account's XP
    // that xp.get would return differently.
    private void NotifyPluginsOfDataChanges()
    {
        var accounts = string.Join('|', _accounts.Select(
            account => $"{account.Id}:{account.Label}:{_openAccountIds.Contains(account.Id)}"));
        if (accounts != _postedAccounts)
        {
            _postedAccounts = accounts;
            _communityPlugins.PostEvent("accounts.changed", null);
        }

        foreach (var state in _xpTracker.GetStates())
        {
            var current = (state.LastUpdated, state.IsStale, state.RatePerHour, state.SessionGain);
            // Equals, not ==, so a NaN rate counts as unchanged instead of posting on every refresh.
            if (!_postedXpUpdates.TryGetValue(state.AccountId, out var posted) || !posted.Equals(current))
            {
                _postedXpUpdates[state.AccountId] = current;
                _communityPlugins.PostEvent("xp.updated", new { accountId = state.AccountId });
            }
        }
    }

    // A NaN or infinite number can't be written as JSON, so it goes to plugins as null.
    private static double? Finite(double? value) => value is { } number && double.IsFinite(number) ? number : null;

    // The name plugins may see. With no ranking name saved, the profile is looked up by the saved login, so the
    // name read from it is the login and stays private.
    internal static string? PublicInGameName(string? rankingUsername, string? profileUsername) =>
        string.IsNullOrWhiteSpace(rankingUsername) ? null : profileUsername ?? rankingUsername;

    IReadOnlyList<PluginAccountInfo> IPluginHostData.GetAccounts() =>
        _accounts.Select(account => new PluginAccountInfo(
            account.Id,
            account.Label,
            PublicInGameName(account.RankingUsername, _xpTracker.GetLatestSnapshot(account.Id)?.Username),
            _openAccountIds.Contains(account.Id))).ToArray();

    PluginXpInfo? IPluginHostData.GetXp(Guid accountId)
    {
        if (_accounts.All(account => account.Id != accountId))
        {
            return null;
        }

        var state = _xpTracker.GetStates().FirstOrDefault(candidate => candidate.AccountId == accountId);
        var snapshot = _xpTracker.GetLatestSnapshot(accountId);
        var active = snapshot?.ActiveClassName is { } className && snapshot.Classes.TryGetValue(className, out var found)
            ? found
            : null;
        return new PluginXpInfo(
            state?.ActiveClass ?? snapshot?.ActiveClassName,
            active?.Level,
            active?.CurrentXp,
            active?.NextLevelXp,
            state?.XpUntilNextLevel,
            Finite(state?.HoursUntilNextLevel),
            Finite(state?.RatePerHour),
            state?.SessionGain ?? 0,
            snapshot?.Classes
                .Select(entry => new PluginClassInfo(
                    entry.Key, entry.Value.Level, entry.Value.CurrentXp, entry.Value.NextLevelXp))
                .ToArray() ?? [],
            state?.LastUpdated,
            // No XP data yet reads as stale, for an open account as for a closed one.
            state is null || state.IsStale || state.LastUpdated is null);
    }

    PluginStatsInfo? IPluginHostData.GetStats(Guid accountId)
    {
        var snapshot = _xpTracker.GetLatestSnapshot(accountId);
        return snapshot?.ActiveClassName is { } className && snapshot.Classes.TryGetValue(className, out var active)
            ? new PluginStatsInfo(className, active.Level, active.Hp, active.Sp, active.Attack, active.Magic,
                active.Skill, active.Speed, active.Luck, active.Defense, active.Resistance)
            : null;
    }

    PluginTimerInfo IPluginHostData.GetTimer()
    {
        var snapshot = _timer.Snapshot();
        return new PluginTimerInfo(
            snapshot.State switch
            {
                SpeedrunTimerState.Running => "running",
                SpeedrunTimerState.Finished => "finished",
                _ => "ready"
            },
            (long)snapshot.Total.TotalMilliseconds,
            snapshot.Laps.Select(lap => new PluginLapInfo(
                lap.Number, (long)lap.LapTime.TotalMilliseconds, (long)lap.SplitTotal.TotalMilliseconds)).ToArray());
    }

    bool IPluginHostData.IsPanelShowing(string pluginId) => PluginSidebar.IsShowing(pluginId);

    // The shell gets AbsoluteUri, never ToString(), which un-escapes %22 into a quote and could add an argument.
    void IPluginHostData.OpenInBrowser(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            GlobalStatusText.Text = "The link couldn't be opened.";
        }
    }
}
