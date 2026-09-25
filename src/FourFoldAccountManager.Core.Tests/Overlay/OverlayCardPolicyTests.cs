using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Overlay;

public sealed class OverlayCardPolicyTests
{
    private static readonly OverlayBounds First = new(0.1, 0.1, 0.2, 0.1);
    private static readonly OverlayBounds Second = new(0.5, 0.5, 0.2, 0.1);

    [Fact]
    public void CatalogRegistersEveryAddOnInPanelOrder()
    {
        Assert.Equal(
            [OverlayAddOnKind.Xp, OverlayAddOnKind.Stats, OverlayAddOnKind.XpCalc, OverlayAddOnKind.Timer],
            OverlayAddOnCatalog.All.Select(d => d.Kind));
        Assert.Equal((0, 1, 2, 3),
            ((int)OverlayAddOnKind.Xp, (int)OverlayAddOnKind.Timer, (int)OverlayAddOnKind.Stats, (int)OverlayAddOnKind.XpCalc));

        var expected = new (OverlayAddOnKind Kind, OverlayAddOnScope Scope, string Name, double W, double H, double MinW, double MinH)[]
        {
            (OverlayAddOnKind.Xp, OverlayAddOnScope.Account, "XP/hr", 200, 52, 144, 40),
            (OverlayAddOnKind.Stats, OverlayAddOnScope.Account, "Stats", 260, 220, 180, 150),
            (OverlayAddOnKind.XpCalc, OverlayAddOnScope.Account, "XP calc", 220, 56, 150, 40),
            (OverlayAddOnKind.Timer, OverlayAddOnScope.Global, "Timer", 220, 60, 150, 44)
        };
        foreach (var entry in expected)
        {
            Assert.True(OverlayAddOnCatalog.TryGet(entry.Kind, out var definition));
            Assert.Equal((entry.Scope, entry.Name, entry.W, entry.H, entry.MinW, entry.MinH),
                (definition.Scope, definition.DisplayName, definition.DefaultWidth, definition.DefaultHeight,
                    definition.MinimumWidth, definition.MinimumHeight));
        }

        Assert.False(OverlayAddOnCatalog.TryGet((OverlayAddOnKind)99, out _));
        Assert.Equal(-1, OverlayAddOnCatalog.IndexOf((OverlayAddOnKind)99));
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
}
