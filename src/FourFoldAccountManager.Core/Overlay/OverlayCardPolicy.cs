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

            result.Add(card with
            {
                Bounds = card.Bounds is { IsValid: true } ? card.Bounds : null,
                StatsWindowBounds = card.StatsWindowBounds is { IsValid: true } ? card.StatsWindowBounds : null
            });
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

    public static OverlayCardPlacement? Get(PanelSettings settings, OverlayCardKey key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.OverlayCards.FirstOrDefault(card => card.Key == key);
    }

    // Showing a card over the game takes it out of the stats window, so a card is only ever in one place.
    public static PanelSettings WithEnabled(PanelSettings settings, OverlayCardKey key, bool enabled) =>
        Upsert(settings, key, existing =>
            (existing ?? new OverlayCardPlacement(key.Kind, key.AccountId, false, null)) with
            {
                Enabled = enabled,
                InStatsWindow = false
            });

    // Ticking a card in the stats window shows it there and takes it off the game; unticking switches it off.
    public static PanelSettings WithStatsWindow(PanelSettings settings, OverlayCardKey key, bool show) =>
        Upsert(settings, key, existing =>
            (existing ?? new OverlayCardPlacement(key.Kind, key.AccountId, false, null)) with
            {
                Enabled = show,
                InStatsWindow = show
            });

    public static PanelSettings WithStatsWindowBounds(PanelSettings settings, OverlayCardKey key, OverlayBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (!bounds.IsValid)
        {
            throw new ArgumentException("Overlay bounds must be valid and within the normalized viewport.", nameof(bounds));
        }

        return Upsert(settings, key, existing =>
            (existing ?? new OverlayCardPlacement(key.Kind, key.AccountId, true, null) { InStatsWindow = true }) with
            {
                StatsWindowBounds = bounds
            });
    }

    public static PanelSettings WithBounds(PanelSettings settings, OverlayCardKey key, OverlayBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (!bounds.IsValid)
        {
            throw new ArgumentException("Overlay bounds must be valid and within the normalized viewport.", nameof(bounds));
        }

        return Upsert(settings, key, existing =>
            (existing ?? new OverlayCardPlacement(key.Kind, key.AccountId, true, null)) with { Bounds = bounds });
    }

    private static PanelSettings Upsert(
        PanelSettings settings,
        OverlayCardKey key,
        Func<OverlayCardPlacement?, OverlayCardPlacement> update)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsValidKey(key))
        {
            throw new ArgumentException("The overlay card key does not match a registered add-on.", nameof(key));
        }

        var cards = settings.OverlayCards.ToList();
        var index = cards.FindIndex(card => card.Key == key);
        var next = update(index >= 0 ? cards[index] : null);
        if (index >= 0)
        {
            cards[index] = next;
        }
        else
        {
            cards.Add(next);
        }

        return settings with { OverlayCards = Array.AsReadOnly(cards.ToArray()) };
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
