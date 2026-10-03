using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Core.Plugins;

public sealed record PluginCardManifest(string Id, string Name, OverlayAddOnScope Scope);

// A validated plugin.json. Sites are origins (scheme, host and port only).
public sealed record PluginManifest(
    string Id,
    string Name,
    string ShortLabel,
    string Version,
    string Author,
    string Description,
    int ApiVersion,
    string Panel,
    string? Icon,
    IReadOnlyList<Uri> Sites,
    bool AnySite,
    IReadOnlyList<PluginCardManifest> Cards)
{
    // The plugin's folder on disk.
    public string Folder { get; init; } = string.Empty;
}

public sealed record PluginManifestResult(PluginManifest? Manifest, string? Error);
