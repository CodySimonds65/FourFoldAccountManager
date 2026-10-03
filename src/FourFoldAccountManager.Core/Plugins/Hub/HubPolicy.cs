using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Plugins.Hub;

// A plugin installed from the hub, at the commit that was reviewed.
public sealed record HubInstalled(string Id, string Commit);

// An installed plugin that may run, and with what trust.
public sealed record HubLoad(string Id, string Folder, string Commit, PluginTrust Trust);

// The decisions about hub plugins that need no files and no network.
public static class HubPolicy
{
    public const string UnlistedReason = "This plugin is no longer on the hub.";

    public const string RemovedReason = "This plugin was removed from the hub.";

    // Why an installed plugin may not run, or null when it may. A removal wins even when the catalog also lists the id,
    // and a plugin the catalog doesn't list at all counts as pulled, so a mistake on the hub's side stops a plugin and
    // never leaves an unlisted one running. With no catalog yet, nothing is known to be pulled.
    public static string? PulledReason(HubCatalog? catalog, string id)
    {
        if (catalog is null)
        {
            return null;
        }

        var removal = catalog.Removed.FirstOrDefault(removed => removed.Id == id);
        if (removal is not null)
        {
            return removal.Reason.Length > 0 ? removal.Reason : RemovedReason;
        }

        return catalog.Plugins.Any(plugin => plugin.Id == id) ? null : UnlistedReason;
    }

    // The installed plugins that may run. A plugin gets any-website trust only when the catalog clears the exact
    // commit that is installed: with no catalog, or while a cleared update is still to be installed, it gets its
    // declared sites only.
    public static IReadOnlyList<HubLoad> Loads(
        HubCatalog? catalog, IReadOnlyList<HubInstalled> installed, Func<string, string> folderOf) =>
        installed
            .Where(plugin => PulledReason(catalog, plugin.Id) is null)
            .Select(plugin =>
            {
                var listed = catalog?.Plugins.FirstOrDefault(candidate => candidate.Id == plugin.Id);
                return new HubLoad(
                    plugin.Id, folderOf(plugin.Id), plugin.Commit,
                    listed is { AnySite: true } && listed.Commit == plugin.Commit
                        ? PluginTrust.Verified
                        : PluginTrust.Standard);
            })
            .ToArray();

    // While developer mode has a plugin with the same id in the dev folder, that copy runs instead of the hub's.
    public static IReadOnlyList<HubLoad> Visible(IReadOnlyList<HubLoad> loads, IReadOnlySet<string> devIds) =>
        loads.Where(load => !devIds.Contains(load.Id)).ToArray();

    // Listed plugins whose reviewed commit differs from the one installed.
    public static IReadOnlyList<HubPlugin> Updates(HubCatalog? catalog, IReadOnlyList<HubInstalled> installed) =>
        catalog is null
            ? []
            : catalog.Plugins
                .Where(plugin => installed.Any(other => other.Id == plugin.Id && other.Commit != plugin.Commit))
                .ToArray();

    // Forgets an uninstalled plugin in saved settings: its cards, its place in the strip, and its switch. When it was
    // the open plugin the panel closes, as when it is switched off. Returns the same instance when there was nothing
    // to forget, so callers can skip the save.
    public static PanelSettings WithUninstalled(PanelSettings settings, string id)
    {
        var cardPrefix = id + "/";
        var cards = settings.OverlayCards
            .Where(card => card.PluginCard?.StartsWith(cardPrefix, StringComparison.Ordinal) != true)
            .ToArray();
        var order = settings.PluginOrder.Where(other => other != id).ToArray();
        var disabled = settings.DisabledPlugins.Where(other => other != id).ToArray();
        if (cards.Length == settings.OverlayCards.Count && order.Length == settings.PluginOrder.Count &&
            disabled.Length == settings.DisabledPlugins.Count && settings.OpenPlugin != id)
        {
            return settings;
        }

        var wasOpen = settings.OpenPlugin == id;
        return settings with
        {
            OverlayCards = Array.AsReadOnly(cards),
            PluginOrder = Array.AsReadOnly(order),
            DisabledPlugins = Array.AsReadOnly(disabled),
            OpenPlugin = wasOpen ? null : settings.OpenPlugin,
            PluginsSidebarExpanded = !wasOpen && settings.PluginsSidebarExpanded
        };
    }

    // The hub page's list: installed plugins first, then by name. An empty query matches everything.
    public static IReadOnlyList<HubPlugin> Search(
        IReadOnlyList<HubPlugin> plugins, string? query, IReadOnlySet<string> installed)
    {
        var text = query?.Trim() ?? string.Empty;
        return plugins
            .Where(plugin => text.Length == 0 ||
                             plugin.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                             plugin.Author.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                             plugin.Description.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(plugin => installed.Contains(plugin.Id) ? 0 : 1)
            .ThenBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plugin => plugin.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
