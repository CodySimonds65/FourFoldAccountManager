using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Overlay;

public sealed class OverlayCardPolicyTests
{
    private static readonly OverlayBounds First = new(0.1, 0.1, 0.2, 0.1);
    private static readonly OverlayBounds Second = new(0.5, 0.5, 0.2, 0.1);

    [Fact]
    public void CatalogRegistersOnlyTheAccountScopedXpCard()
    {
        var definition = Assert.Single(OverlayAddOnCatalog.All);
        Assert.Equal(OverlayAddOnKind.Xp, definition.Kind);
        Assert.Equal(OverlayAddOnScope.Account, definition.Scope);
        Assert.Equal("XP/hr", definition.DisplayName);
        Assert.Equal((200d, 52d, 144d, 40d),
            (definition.DefaultWidth, definition.DefaultHeight, definition.MinimumWidth, definition.MinimumHeight));
        Assert.False(OverlayAddOnCatalog.TryGet((OverlayAddOnKind)99, out _));
        Assert.Equal(-1, OverlayAddOnCatalog.IndexOf((OverlayAddOnKind)99));
    }

    [Fact]
    public void KeysMustMatchTheirAddOnScope()
    {
        Assert.True(OverlayCardPolicy.IsValidKey(new OverlayCardKey(OverlayAddOnKind.Xp, Guid.NewGuid())));
        Assert.False(OverlayCardPolicy.IsValidKey(new OverlayCardKey(OverlayAddOnKind.Xp, null)));
        Assert.False(OverlayCardPolicy.IsValidKey(new OverlayCardKey(OverlayAddOnKind.Xp, Guid.Empty)));
        Assert.False(OverlayCardPolicy.IsValidKey(new OverlayCardKey((OverlayAddOnKind)99, Guid.NewGuid())));
    }

    [Fact]
    public void NormalizeDropsUnknownKindsScopeMismatchesNullsAndDuplicates()
    {
        var accountId = Guid.NewGuid();
        var kept = new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, true, First);

        var result = OverlayCardPolicy.Normalize(
        [
            null,
            kept,
            kept with { Enabled = false, Bounds = Second },
            new OverlayCardPlacement((OverlayAddOnKind)99, accountId, true, First),
            new OverlayCardPlacement(OverlayAddOnKind.Xp, null, true, First),
            new OverlayCardPlacement(OverlayAddOnKind.Xp, Guid.Empty, true, First)
        ], legacyXpBounds: null);

        Assert.Equal([kept], result);
    }

    [Fact]
    public void NormalizeClearsInvalidBoundsButKeepsTheCard()
    {
        var accountId = Guid.NewGuid();

        var result = OverlayCardPolicy.Normalize(
            [new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, true, new OverlayBounds(0.9, 0, 0.5, 0.5))],
            legacyXpBounds: null);

        Assert.Equal([new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, true, null)], result);
    }

    [Fact]
    public void NormalizeMigratesLegacyXpBoundsAsEnabledCardsWithoutOverridingExistingOnes()
    {
        var existing = Guid.NewGuid();
        var migrated = Guid.NewGuid();
        var invalid = Guid.NewGuid();
        var current = new OverlayCardPlacement(OverlayAddOnKind.Xp, existing, false, Second);

        var result = OverlayCardPolicy.Normalize([current], new Dictionary<Guid, OverlayBounds>
        {
            [existing] = First,
            [migrated] = First,
            [invalid] = new OverlayBounds(0.9, 0, 0.5, 0.5),
            [Guid.Empty] = First
        });

        Assert.Equal(
        [
            current,
            new OverlayCardPlacement(OverlayAddOnKind.Xp, migrated, true, First),
            new OverlayCardPlacement(OverlayAddOnKind.Xp, invalid, true, null)
        ], result);
    }

    [Fact]
    public void RemoveAccountDropsOnlyThatAccountsCards()
    {
        var removed = Guid.NewGuid();
        var kept = new OverlayCardPlacement(OverlayAddOnKind.Xp, Guid.NewGuid(), true, First);

        var result = OverlayCardPolicy.RemoveAccount(
            [new OverlayCardPlacement(OverlayAddOnKind.Xp, removed, true, Second), kept], removed);

        Assert.Equal([kept], result);
    }

    [Fact]
    public void DefaultBoundsPlaceAccountCardsTopLeftAndCascade()
    {
        var xp = OverlayAddOnCatalog.All[0];

        var first = OverlayCardPolicy.DefaultBounds(xp, 1000, 500, cascadeIndex: 0);
        var second = OverlayCardPolicy.DefaultBounds(xp, 1000, 500, cascadeIndex: 1);

        Assert.Equal(0.012, first.X, 6);
        Assert.Equal(0.024, first.Y, 6);
        Assert.Equal(0.2, first.Width, 6);
        Assert.Equal(0.104, first.Height, 6);
        Assert.Equal(0.028, second.X, 6);
        Assert.Equal(0.056, second.Y, 6);
    }

    [Fact]
    public void DefaultBoundsCentreGlobalCardsHorizontally()
    {
        var global = new OverlayAddOnDefinition(OverlayAddOnKind.Xp, OverlayAddOnScope.Global, "Test", 200, 50, 100, 30);

        var bounds = OverlayCardPolicy.DefaultBounds(global, 1000, 500, cascadeIndex: 0);

        Assert.Equal(0.4, bounds.X, 6);
        Assert.Equal(0.024, bounds.Y, 6);
    }

    [Fact]
    public void DefaultBoundsFitLayersSmallerThanTheCard()
    {
        var bounds = OverlayCardPolicy.DefaultBounds(OverlayAddOnCatalog.All[0], 100, 30, cascadeIndex: 3);

        Assert.True(bounds.IsValid);
        Assert.Equal(new OverlayBounds(0, 0, 1, 1), bounds);
    }

    [Fact]
    public void DefaultBoundsRejectUnmeasuredLayers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayCardPolicy.DefaultBounds(OverlayAddOnCatalog.All[0], 0, 500, cascadeIndex: 0));
    }
}
