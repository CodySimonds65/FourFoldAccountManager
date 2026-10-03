using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Plugins.Web;

public sealed record PluginCardRow(string Label, string Value, double? Progress);

public sealed record PluginCardContent(string Summary, IReadOnlyList<PluginCardRow> Rows);

// The contents community plugins have set for their cards. Contents stay after a plugin stops, so its cards keep
// showing their last data (drawn stale) until it runs again.
public sealed class PluginCardStore
{
    private readonly Dictionary<OverlayCardKey, PluginCardContent> _cards = [];

    public event Action? Changed;

    public PluginCardContent? Get(OverlayCardKey key) => _cards.GetValueOrDefault(key);

    public void Set(OverlayCardKey key, PluginCardContent content)
    {
        _cards[key] = content;
        Changed?.Invoke();
    }

    public void Clear(OverlayCardKey key)
    {
        if (_cards.Remove(key))
        {
            Changed?.Invoke();
        }
    }
}
