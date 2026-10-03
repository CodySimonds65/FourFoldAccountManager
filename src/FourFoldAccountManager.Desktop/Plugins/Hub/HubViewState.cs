using FourFoldAccountManager.Core.Plugins.Hub;

namespace FourFoldAccountManager.Desktop.Plugins.Hub;

// An installed plugin the hub no longer lists, with the reason to show.
public sealed record HubPulledPlugin(string Id, string Name, string Reason);

// Everything the hub page and the plugin list show about the hub.
public sealed record HubViewState(
    bool Loading,
    bool Unreachable,
    IReadOnlyList<HubPlugin> Plugins,
    IReadOnlySet<string> Installed,
    // Plugins being installed, updated or removed right now.
    IReadOnlySet<string> Busy,
    // The reason a plugin's last install or removal failed, by plugin id.
    IReadOnlyDictionary<string, string> Errors,
    IReadOnlyList<HubPulledPlugin> Pulled)
{
    public static HubViewState Empty { get; } = new(
        false, false, [], new HashSet<string>(), new HashSet<string>(), new Dictionary<string, string>(), []);
}
