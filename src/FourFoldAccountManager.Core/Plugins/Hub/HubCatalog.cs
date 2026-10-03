using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Core.Plugins.Hub;

public sealed record HubCard(string Name, OverlayAddOnScope Scope);

// One listed plugin: what the hub page shows, and the reviewed commit, size and hash of its package.
public sealed record HubPlugin(
    string Id,
    string Name,
    string ShortLabel,
    string Version,
    string Author,
    string Description,
    IReadOnlyList<Uri> Sites,
    // Already the combined answer: the maintainers cleared it and the plugin's plugin.json asks for it.
    bool AnySite,
    IReadOnlyList<HubCard> Cards,
    Uri Repository,
    string Commit,
    // yyyy-MM-dd, or empty when the catalog's value couldn't be read.
    string Reviewed,
    long Size,
    string Sha256);

// A plugin the maintainers pulled, and why.
public sealed record HubRemoval(string Id, string Reason);

public sealed record HubCatalog(IReadOnlyList<HubPlugin> Plugins, IReadOnlyList<HubRemoval> Removed);
