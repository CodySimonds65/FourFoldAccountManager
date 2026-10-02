using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

public readonly record struct OverlayCardKey(OverlayAddOnKind Kind, Guid? AccountId);

public sealed record OverlayCardPlacement(
    OverlayAddOnKind Kind,
    Guid? AccountId,
    bool Enabled,
    OverlayBounds? Bounds)
{
    [JsonIgnore]
    public OverlayCardKey Key => new(Kind, AccountId);

    // Shown in the stats window instead of over the game. Only meaningful while Enabled.
    public bool InStatsWindow { get; init; }

    // The card's position in the stats window, kept apart from Bounds so a card moved between the two places
    // returns to where it was in each.
    public OverlayBounds? StatsWindowBounds { get; init; }
}
