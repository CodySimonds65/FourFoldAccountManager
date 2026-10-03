using System.Diagnostics;
using System.IO;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Plugins;
using FourFoldAccountManager.Core.Timing;
using FourFoldAccountManager.Desktop.Plugins;
using FourFoldAccountManager.Desktop.Plugins.Web;

namespace FourFoldAccountManager.Desktop;

// Community plugins: loading, the data they may read, and the events they are sent.
public partial class MainWindow : IPluginHostData
{
    private readonly PluginCardStore _pluginCards = new();
    private readonly Dictionary<Guid, (DateTimeOffset? LastUpdated, bool IsStale, double? RatePerHour, long SessionGain)>
        _postedXpUpdates = [];
    private CommunityPluginManager _communityPlugins = null!;
    private string _postedAccounts = string.Empty;

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

    private void RefreshPluginSidebar()
    {
        PluginSidebar.SetPlugins(AllPlugins);
        PluginSidebar.SetCommunityState(_communityPlugins.Rejected, _communityPlugins.StartupError);
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
            state?.IsStale ?? true);
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
