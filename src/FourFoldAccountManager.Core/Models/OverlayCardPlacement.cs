using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

public readonly record struct OverlayCardKey(OverlayAddOnKind Kind, Guid? AccountId, string? PluginCard = null);

public sealed record OverlayCardPlacement(
    OverlayAddOnKind Kind,
    Guid? AccountId,
    bool Enabled,
    OverlayBounds? Bounds)
{
    [JsonIgnore]
    public OverlayCardKey Key => new(Kind, AccountId, PluginCard);

    // "<pluginId>/<cardId>" when Kind is Plugin; null for FourFold's own cards.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginCard { get; init; }

    // Shown as its own floating window instead of over the game. Only meaningful while Enabled.
    public bool IsFloating { get; init; }

    // The floating window's spot on the desktop, kept apart from Bounds so a card moved between the two places
    // returns to where it was in each.
    public FloatingCardBounds? FloatingBounds { get; init; }
}
