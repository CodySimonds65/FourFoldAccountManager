using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Overlay;

public static class OverlayCardPolicy
{
    public const double DefaultMargin = 12;

    public const double CascadeStep = 16;

    public static bool IsValidKey(OverlayCardKey key) =>
        OverlayAddOnCatalog.TryGet(key.Kind, out var definition) &&
        (definition.Scope == OverlayAddOnScope.Account
            ? key.AccountId is { } accountId && accountId != Guid.Empty
            : key.AccountId is null);

    // Settings files may be old or hand-edited; normalize instead of rejecting them.
    public static IReadOnlyList<OverlayCardPlacement> Normalize(
        IEnumerable<OverlayCardPlacement?>? cards,
        IReadOnlyDictionary<Guid, OverlayBounds>? legacyXpBounds)
    {
        var result = new List<OverlayCardPlacement>();
        var keys = new HashSet<OverlayCardKey>();
        foreach (var card in cards ?? [])
        {
            if (card is null || !IsValidKey(card.Key) || !keys.Add(card.Key))
            {
                continue;
            }

            result.Add(card.Bounds is { IsValid: false } ? card with { Bounds = null } : card);
        }

        foreach (var (accountId, bounds) in legacyXpBounds ?? new Dictionary<Guid, OverlayBounds>())
        {
            var key = new OverlayCardKey(OverlayAddOnKind.Xp, accountId);
            if (bounds is null || !IsValidKey(key) || !keys.Add(key))
            {
                continue;
            }

            result.Add(new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, true, bounds.IsValid ? bounds : null));
        }

        return Array.AsReadOnly(result.ToArray());
    }

    public static IReadOnlyList<OverlayCardPlacement> RemoveAccount(
        IReadOnlyList<OverlayCardPlacement> cards,
        Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return Array.AsReadOnly(cards.Where(card => card.AccountId != accountId).ToArray());
    }

    public static OverlayBounds DefaultBounds(
        OverlayAddOnDefinition definition,
        double layerWidth,
        double layerHeight,
        int cascadeIndex)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!double.IsFinite(layerWidth) || !double.IsFinite(layerHeight) || layerWidth <= 0 || layerHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(layerWidth), "The overlay layer must have a positive size.");
        }

        var width = Math.Min(definition.DefaultWidth, layerWidth);
        var height = Math.Min(definition.DefaultHeight, layerHeight);
        var offset = Math.Max(0, cascadeIndex) * CascadeStep;
        var left = definition.Scope == OverlayAddOnScope.Global
            ? (layerWidth - width) / 2d + offset
            : DefaultMargin + offset;
        var top = DefaultMargin + offset;
        return new OverlayBounds(left / layerWidth, top / layerHeight, width / layerWidth, height / layerHeight)
            .ClampToViewport();
    }
}
