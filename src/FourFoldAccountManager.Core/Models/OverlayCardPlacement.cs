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
}
