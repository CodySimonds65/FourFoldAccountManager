using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Core.Plugins;

public sealed record PluginCardDescriptor(string Id, string Name, OverlayAddOnScope Scope);

// Everything the strip, the plugin list and the "off" rules need to know about a plugin, with no UI.
public sealed record PluginDescriptor(
    string Id,
    string Name,
    string ShortLabel,
    string Icon,
    IReadOnlyList<OverlayAddOnKind> OverlayCards,
    IReadOnlyList<GlobalShortcutAction> Shortcuts)
{
    // The cards a community plugin declared. FourFold's own plugins use OverlayCards instead.
    public IReadOnlyList<PluginCardDescriptor> Cards { get; init; } = [];

    // A community plugin's icon image; when null the strip shows the Icon glyph.
    public string? IconPath { get; init; }

    // A short tag beside the plugin's name in the plugin list, such as "DEV".
    public string? Badge { get; init; }

    // Text under the plugin's name in the plugin list. A community plugin gives two lines: its author, then the sites
    // it can contact.
    public string? Detail { get; init; }
}
