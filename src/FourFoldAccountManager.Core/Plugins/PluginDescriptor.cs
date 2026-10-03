using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Plugins;

// Everything the strip, the plugin list and the "off" rules need to know about a plugin, with no UI.
// A community plugin's manifest will load into this same shape.
public sealed record PluginDescriptor(
    string Id,
    string Name,
    string ShortLabel,
    string Icon,
    IReadOnlyList<OverlayAddOnKind> OverlayCards,
    IReadOnlyList<GlobalShortcutAction> Shortcuts);
