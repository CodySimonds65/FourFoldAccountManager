using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Plugins;

public static class PluginLayoutPolicy
{
    public const int MaximumIds = 64;

    // Known plugins in saved order; a known plugin missing from the saved order goes last, in the order given.
    public static IReadOnlyList<PluginDescriptor> Ordered(PanelSettings settings, IReadOnlyList<PluginDescriptor> known)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in settings.PluginOrder)
        {
            rank.TryAdd(id, rank.Count);
        }

        return known
            .Select((plugin, index) => (plugin, index, rank: rank.GetValueOrDefault(plugin.Id, int.MaxValue)))
            .OrderBy(entry => entry.rank)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.plugin)
            .ToArray();
    }

    public static bool IsEnabled(PanelSettings settings, string id) =>
        !settings.DisabledPlugins.Contains(id, StringComparer.Ordinal);

    public static bool IsCardSuppressed(PanelSettings settings, OverlayAddOnKind kind) =>
        BuiltInPlugins.All.Any(plugin => plugin.OverlayCards.Contains(kind) && !IsEnabled(settings, plugin.Id));

    public static bool IsShortcutSuppressed(PanelSettings settings, GlobalShortcutAction action) =>
        BuiltInPlugins.All.Any(plugin => plugin.Shortcuts.Contains(action) && !IsEnabled(settings, plugin.Id));

    // The saved open plugin when it is known and switched on; otherwise null.
    public static PluginDescriptor? OpenPlugin(PanelSettings settings, IReadOnlyList<PluginDescriptor> known) =>
        known.FirstOrDefault(plugin => plugin.Id == settings.OpenPlugin && IsEnabled(settings, plugin.Id));

    // Returns the same instance when nothing changes, so callers can skip the save.
    public static PanelSettings WithEnabled(PanelSettings settings, string id, bool enabled)
    {
        if (IsEnabled(settings, id) == enabled)
        {
            return settings;
        }

        var others = settings.DisabledPlugins.Where(other => other != id);
        var next = settings with
        {
            DisabledPlugins = Array.AsReadOnly((enabled ? others : others.Append(id)).ToArray())
        };
        // Switching off the open plugin closes the panel.
        return !enabled && settings.OpenPlugin == id ? next with { PluginsSidebarExpanded = false } : next;
    }

    // Moves a plugin to index among the strip's icons (switched-on plugins, in order). Switched-off plugins keep their
    // places, and unknown saved ids stay at the end.
    public static PanelSettings WithMoved(
        PanelSettings settings, IReadOnlyList<PluginDescriptor> known, string id, int index)
    {
        var full = Ordered(settings, known).Select(plugin => plugin.Id).ToList();
        var strip = full.Where(other => IsEnabled(settings, other)).ToList();
        if (!strip.Remove(id))
        {
            return settings;
        }

        strip.Insert(Math.Clamp(index, 0, strip.Count), id);
        var nextStripId = 0;
        var reordered = full.Select(other => IsEnabled(settings, other) ? strip[nextStripId++] : other);
        var unknown = settings.PluginOrder.Where(saved => known.All(plugin => plugin.Id != saved));
        var order = reordered.Concat(unknown).ToArray();
        return order.SequenceEqual(settings.PluginOrder)
            ? settings
            : settings with { PluginOrder = Array.AsReadOnly(order) };
    }

    public static PanelSettings WithOpened(PanelSettings settings, string id) =>
        settings.OpenPlugin == id && settings.PluginsSidebarExpanded
            ? settings
            : settings with { OpenPlugin = id, PluginsSidebarExpanded = true };

    public static PanelSettings WithClosed(PanelSettings settings) =>
        settings.PluginsSidebarExpanded ? settings with { PluginsSidebarExpanded = false } : settings;

    // Load-time cleanup: blank ids and repeats go (the first wins), and at most 64 ids stay.
    public static IReadOnlyList<string> CleanIds(IReadOnlyList<string>? ids) =>
        Array.AsReadOnly((ids ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Take(MaximumIds)
            .ToArray());
}
