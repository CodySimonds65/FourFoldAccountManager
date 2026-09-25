# Full-Screen Overlay Add-Ons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the XP-only full-screen overlay into an add-on host. Each plugin card has an on/off switch and a draggable position. Move the existing XP/hr card into this system with no visible change for existing users.

**Architecture:** Core owns the pieces that don't need a UI: an add-on catalog, per-card placements stored in `PanelSettings.OverlayCards`, and a pure `OverlayCardPolicy` for normalization, migration, toggling, and default placement. Desktop adds a reusable `OverlayCardFrame` (chrome and move/resize thumbs, with content chosen by an implicit `DataTemplate`) and an `OverlayCardLayer` canvas that hosts many frames keyed by `OverlayCardKey`. There is one layer per client slot and one window-wide layer. `FullscreenOverlayTray` replaces the drag-an-account tray with per-account on/off switches.

**Tech Stack:** .NET 10, WPF (net10.0-windows), System.Text.Json, xUnit 2.9.

**Spec:** `docs/superpowers/specs/2026-09-25-overlay-add-ons-design.md`

## Global Constraints

- Work on branch `feature/overlay-add-ons` (already created from `main`; the spec is committed).
- `OverlayAddOnKind` is persisted as a number. Never renumber existing values; add new kinds with new numbers.
- Only `OverlayAddOnKind.Xp` (Account scope, "XP/hr", default 200×52 px, minimum 144×40 px) is registered in this project. Do not add Timer, Stats, or XP calc kinds.
- Every place that constructs `PanelSettings` field by field must copy `OverlayCards`. Today these are `SettingsStore.Validate` and `PanelLayoutPolicy.WithLayout`, `Assign`, `ClearAccount`, and `CopySettings`.
- Loading settings must never throw because of overlay data. Unknown kinds, scope mismatches, duplicates, and invalid bounds are normalized away.
- The legacy JSON property `xpOverlayBoundsByAccount` is read for migration and never written.
- The persisted property name `RevealXpOverlayTabShortcut` is unchanged. Only its Settings label text changes to "Reveal overlays tab".
- Overlay layers are hit-test visible only in edit mode. Layers have no background, so empty layer space never takes input.
- New accounts have the XP card switched off. Migrated XP placements are switched on.
- Build and test commands run from the repository root:
  - `dotnet build FourFoldAccountManager.sln -c Release`
  - `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
  - `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`

## Review Focus

- **Hand-edited or older settings files** (`"overlayCards": null`, unknown kind numbers, or legacy bounds with invalid values) must load without error. They are covered in Task 2 tests.
- **Window-wide layer in edit mode.** Clicks on empty space must fall through to the per-client layers and the game, so users can still drag per-account cards. This is covered in Task 3 by a hit-test on empty space.
- **Leaving full screen or switching a card off mid-drag.** The late `DragCompleted` must not save bounds for a card that is gone. This is covered in Task 3.
- **Switching a card on before the layer has a size.** No exception, and the card appears at the default position once measured. This is covered in Task 3.
- **Failed toggle save.** The switch must visually revert. The tray rebuilds its rows from the persisted settings after every toggle attempt, so a stale checkbox state is replaced. This is covered in Task 4 (rows are replaced by `SetRows`) and wired in Task 5 (`finally { RefreshTrackerRows(); }`).

---

## File Structure

**Core (`src/FourFoldAccountManager.Core`)**
- `Models/OverlayBounds.cs`: renamed from `XpOverlayBounds.cs`; the record is renamed `OverlayBounds`.
- `Models/OverlayAddOnKind.cs` (new): the persisted add-on kind enum.
- `Models/OverlayCardPlacement.cs` (new): `OverlayCardKey` and `OverlayCardPlacement`.
- `Overlay/OverlayAddOnCatalog.cs` (new): `OverlayAddOnScope`, `OverlayAddOnDefinition`, `OverlayAddOnCatalog`.
- `Overlay/OverlayCardPolicy.cs` (new): normalization, migration, key validation, toggling, bounds updates, account removal, default placement.
- `Models/PanelSettings.cs`: adds `OverlayCards` and a legacy migration property, and removes `XpOverlayBoundsByAccount`.
- `Data/SettingsStore.cs`: `Validate` normalizes `OverlayCards`.
- `Panel/PanelLayoutPolicy.cs`: copies `OverlayCards`, `ClearAccount` removes the account's cards, and the XP-bounds helpers are removed.

**Desktop (`src/FourFoldAccountManager.Desktop`)**
- `Views/OverlayCardFrame.xaml(.cs)` (new): shared card chrome.
- `Views/OverlayCardLayer.cs` (new): multi-card canvas, plus `OverlayCardModel` and `OverlayCardBoundsCommittedEventArgs`.
- `Views/XpOverlayCardData.cs` (new): the XP card data record.
- `Resources/OverlayCardTemplates.xaml` (new): card content templates. It is merged in `App.xaml`.
- `Views/FullscreenOverlayTray.xaml(.cs)` (new): the Overlays panel, plus `OverlayTraySwitch`, `OverlayTrayAccountRow`, and `OverlayCardToggleRequestedEventArgs`.
- `MainWindow.xaml(.cs)`: uses the new layers and tray.
- `Views/SettingsDialog.xaml`: label text change.
- Deleted in Task 5: `Views/XpOverlayLayer.cs`, `Views/XpOverlayCard.xaml(.cs)`, `Views/XpOverlayAccountChoice.cs`, `Views/FullscreenXpOverlayTray.xaml(.cs)`.

**Tests**
- `src/FourFoldAccountManager.Core.Tests/Overlay/OverlayCardPolicyTests.cs` (new)
- `src/FourFoldAccountManager.Core.Tests/Data/SettingsStoreOverlayTests.cs` (new)
- `src/FourFoldAccountManager.Core.Tests/Panel/PanelLayoutPolicyTests.cs` (extended)
- `src/FourFoldAccountManager.Desktop.Tests/WpfTestHost.cs` (new): one shared STA dispatcher with the app resources loaded.
- `src/FourFoldAccountManager.Desktop.Tests/Views/OverlayCardLayerTests.cs` (new)
- `src/FourFoldAccountManager.Desktop.Tests/Views/FullscreenOverlayTrayTests.cs` (new)
- `src/FourFoldAccountManager.Desktop.Tests/Views/ExperienceCalculatorPanelTests.cs`: moved onto `WpfTestHost`.

---

### Task 1: Overlay add-on model, catalog, and list policy (Core)

**Files:**
- Rename: `src/FourFoldAccountManager.Core/Models/XpOverlayBounds.cs` → `src/FourFoldAccountManager.Core/Models/OverlayBounds.cs`
- Modify (mechanical rename of the type `XpOverlayBounds` → `OverlayBounds`): `src/FourFoldAccountManager.Core/Models/PanelSettings.cs`, `src/FourFoldAccountManager.Core/Data/SettingsStore.cs`, `src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs`, `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`, `src/FourFoldAccountManager.Desktop/Views/XpOverlayLayer.cs`
- Create: `src/FourFoldAccountManager.Core/Models/OverlayAddOnKind.cs`
- Create: `src/FourFoldAccountManager.Core/Models/OverlayCardPlacement.cs`
- Create: `src/FourFoldAccountManager.Core/Overlay/OverlayAddOnCatalog.cs`
- Create: `src/FourFoldAccountManager.Core/Overlay/OverlayCardPolicy.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Overlay/OverlayCardPolicyTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `record OverlayBounds(double X, double Y, double Width, double Height)` with `IsValid` and `ClampToViewport()`. These are unchanged from `XpOverlayBounds`.
  - `enum OverlayAddOnKind { Xp = 0 }`
  - `readonly record struct OverlayCardKey(OverlayAddOnKind Kind, Guid? AccountId)`
  - `record OverlayCardPlacement(OverlayAddOnKind Kind, Guid? AccountId, bool Enabled, OverlayBounds? Bounds)` with a `[JsonIgnore] OverlayCardKey Key`.
  - `enum OverlayAddOnScope { Account, Global }`
  - `record OverlayAddOnDefinition(OverlayAddOnKind Kind, OverlayAddOnScope Scope, string DisplayName, double DefaultWidth, double DefaultHeight, double MinimumWidth, double MinimumHeight)`
  - `static class OverlayAddOnCatalog` with:
    - `IReadOnlyList<OverlayAddOnDefinition> All`
    - `bool TryGet(OverlayAddOnKind, out OverlayAddOnDefinition?)`
    - `int IndexOf(OverlayAddOnKind)`
  - `static class OverlayCardPolicy` with:
    - `const double DefaultMargin = 12`
    - `const double CascadeStep = 16`
    - `bool IsValidKey(OverlayCardKey)`
    - `IReadOnlyList<OverlayCardPlacement> Normalize(IEnumerable<OverlayCardPlacement?>? cards, IReadOnlyDictionary<Guid, OverlayBounds>? legacyXpBounds)`
    - `IReadOnlyList<OverlayCardPlacement> RemoveAccount(IReadOnlyList<OverlayCardPlacement> cards, Guid accountId)`
    - `OverlayBounds DefaultBounds(OverlayAddOnDefinition definition, double layerWidth, double layerHeight, int cascadeIndex)`

- [ ] **Step 1: Rename `XpOverlayBounds` to `OverlayBounds`**

Run from the repository root:

```bash
git mv src/FourFoldAccountManager.Core/Models/XpOverlayBounds.cs src/FourFoldAccountManager.Core/Models/OverlayBounds.cs
sed -i -E 's/\bXpOverlayBounds\b/OverlayBounds/g' \
  src/FourFoldAccountManager.Core/Models/OverlayBounds.cs \
  src/FourFoldAccountManager.Core/Models/PanelSettings.cs \
  src/FourFoldAccountManager.Core/Data/SettingsStore.cs \
  src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs \
  src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs \
  src/FourFoldAccountManager.Desktop/Views/XpOverlayLayer.cs
grep -rn "XpOverlayBounds\b" src --include=*.cs | grep -v "/obj/"
```

Expected: the final `grep` prints nothing. `\b` keeps `XpOverlayBoundsByAccount` and `XpOverlayBoundsCommittedEventArgs` unchanged, because they continue with word characters.

- [ ] **Step 2: Write the failing tests**

Create `src/FourFoldAccountManager.Core.Tests/Overlay/OverlayCardPolicyTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release --filter "FullyQualifiedName~OverlayCardPolicyTests"`
Expected: build FAILS with errors such as `The type or namespace name 'Overlay' does not exist` and `The name 'OverlayCardPolicy' does not exist`.

- [ ] **Step 4: Create the model files**

`src/FourFoldAccountManager.Core/Models/OverlayAddOnKind.cs`:

```csharp
namespace FourFoldAccountManager.Core.Models;

// Persisted by number in settings.json; keep existing values stable when adding kinds.
public enum OverlayAddOnKind
{
    Xp = 0
}
```

`src/FourFoldAccountManager.Core/Models/OverlayCardPlacement.cs`:

```csharp
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
```

- [ ] **Step 5: Create the catalog**

`src/FourFoldAccountManager.Core/Overlay/OverlayAddOnCatalog.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Overlay;

public enum OverlayAddOnScope
{
    Account,
    Global
}

public sealed record OverlayAddOnDefinition(
    OverlayAddOnKind Kind,
    OverlayAddOnScope Scope,
    string DisplayName,
    double DefaultWidth,
    double DefaultHeight,
    double MinimumWidth,
    double MinimumHeight);

public static class OverlayAddOnCatalog
{
    // Catalog order is also the stacking order of cards within a layer.
    public static IReadOnlyList<OverlayAddOnDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new OverlayAddOnDefinition(OverlayAddOnKind.Xp, OverlayAddOnScope.Account, "XP/hr", 200, 52, 144, 40)
    });

    public static bool TryGet(OverlayAddOnKind kind, [NotNullWhen(true)] out OverlayAddOnDefinition? definition)
    {
        definition = All.FirstOrDefault(candidate => candidate.Kind == kind);
        return definition is not null;
    }

    public static int IndexOf(OverlayAddOnKind kind)
    {
        for (var index = 0; index < All.Count; index++)
        {
            if (All[index].Kind == kind)
            {
                return index;
            }
        }

        return -1;
    }
}
```

- [ ] **Step 6: Create the list policy**

`src/FourFoldAccountManager.Core/Overlay/OverlayCardPolicy.cs`:

```csharp
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
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: PASS. All Core tests pass, including the 10 new `OverlayCardPolicyTests`.

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add -A src/FourFoldAccountManager.Core src/FourFoldAccountManager.Core.Tests src/FourFoldAccountManager.Desktop
git commit -m "feat: add overlay add-on catalog and placement policy"
```

---

### Task 2: Persist overlay cards in settings (Core)

**Files:**
- Modify: `src/FourFoldAccountManager.Core/Models/PanelSettings.cs`
- Modify: `src/FourFoldAccountManager.Core/Data/SettingsStore.cs` (`Validate`, currently lines 98-164)
- Modify: `src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs` (`WithLayout`, `Assign`, `ClearAccount`, `CopySettings`; remove `GetXpOverlayBounds` and `WithXpOverlayBounds`)
- Modify: `src/FourFoldAccountManager.Core/Overlay/OverlayCardPolicy.cs` (add settings-level helpers)
- Modify (temporary compile fix, replaced in Task 5): `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Data/SettingsStoreOverlayTests.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Panel/PanelLayoutPolicyTests.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Overlay/OverlayCardPolicyTests.cs`

**Interfaces:**
- Consumes (Task 1): `OverlayCardPlacement`, `OverlayCardKey`, `OverlayCardPolicy.Normalize`, `OverlayCardPolicy.RemoveAccount`, `OverlayCardPolicy.IsValidKey`.
- Produces:
  - On `PanelSettings`:
    - `IReadOnlyList<OverlayCardPlacement> OverlayCards { get; init; }`, which defaults to empty.
    - `IReadOnlyDictionary<Guid, OverlayBounds>? LegacyXpOverlayBoundsByAccount { get; init; }`, with JSON name `xpOverlayBoundsByAccount`. It is read for migration and never written.
  - On `OverlayCardPolicy`:
    - `OverlayCardPlacement? Get(PanelSettings settings, OverlayCardKey key)`
    - `PanelSettings WithEnabled(PanelSettings settings, OverlayCardKey key, bool enabled)`
    - `PanelSettings WithBounds(PanelSettings settings, OverlayCardKey key, OverlayBounds bounds)`. This creates an enabled placement when none exists.
  - Removed: `PanelSettings.XpOverlayBoundsByAccount`, `PanelLayoutPolicy.GetXpOverlayBounds`, and `PanelLayoutPolicy.WithXpOverlayBounds`.

- [ ] **Step 1: Write the failing settings-store tests**

Create `src/FourFoldAccountManager.Core.Tests/Data/SettingsStoreOverlayTests.cs`:

```csharp
using System.Text.Json.Nodes;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Data;

public sealed class SettingsStoreOverlayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-settings-{Guid.NewGuid():N}");
    private readonly SettingsStore _store;

    public SettingsStoreOverlayTests()
    {
        Directory.CreateDirectory(_root);
        _store = new SettingsStore(new LocalDataPaths(_root));
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task LegacyXpBoundsLoadAsEnabledXpCardsAndAreNotWrittenBack()
    {
        var accountId = Guid.NewGuid();
        await WriteSettingsAsync(accountId, json =>
        {
            json.Remove("overlayCards");
            json["xpOverlayBoundsByAccount"] = new JsonObject
            {
                [accountId.ToString()] = new JsonObject { ["x"] = 0.1, ["y"] = 0.2, ["width"] = 0.3, ["height"] = 0.1 }
            };
        });

        var loaded = await _store.LoadAsync();

        Assert.Equal(
            [new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, true, new OverlayBounds(0.1, 0.2, 0.3, 0.1))],
            loaded.OverlayCards);
        Assert.Null(loaded.LegacyXpOverlayBoundsByAccount);
        await _store.SaveAsync(loaded);
        Assert.DoesNotContain("xpOverlayBoundsByAccount", await File.ReadAllTextAsync(SettingsPath));
        Assert.Equal(loaded.OverlayCards, (await _store.LoadAsync()).OverlayCards);
    }

    [Fact]
    public async Task OverlayCardsRoundTripThroughSaveAndLoad()
    {
        var accountId = Guid.NewGuid();
        var cards = new[]
        {
            new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, false, new OverlayBounds(0.5, 0.5, 0.2, 0.1))
        };

        await _store.SaveAsync(PanelSettings.Default with
        {
            SlotAccountIds = [accountId, null, null, null, null],
            OverlayCards = cards
        });

        Assert.Equal(cards, (await _store.LoadAsync()).OverlayCards);
    }

    [Fact]
    public async Task UnknownKindsAndScopeMismatchesAreDroppedWithoutFailingTheLoad()
    {
        var accountId = Guid.NewGuid();
        await WriteSettingsAsync(accountId, json => json["overlayCards"] = new JsonArray
        {
            new JsonObject { ["kind"] = 99, ["accountId"] = accountId.ToString(), ["enabled"] = true },
            new JsonObject { ["kind"] = 0, ["accountId"] = null, ["enabled"] = true },
            new JsonObject
            {
                ["kind"] = 0, ["accountId"] = accountId.ToString(), ["enabled"] = true,
                ["bounds"] = new JsonObject { ["x"] = 0.9, ["y"] = 0, ["width"] = 0.5, ["height"] = 0.5 }
            }
        });

        var loaded = await _store.LoadAsync();

        Assert.Equal([new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, true, null)], loaded.OverlayCards);
    }

    [Fact]
    public async Task NullOverlayCardListLoadsAsEmpty()
    {
        await WriteSettingsAsync(Guid.NewGuid(), json => json["overlayCards"] = null);

        Assert.Empty((await _store.LoadAsync()).OverlayCards);
    }

    private async Task WriteSettingsAsync(Guid accountId, Action<JsonObject> edit)
    {
        await _store.SaveAsync(PanelSettings.Default with { SlotAccountIds = [accountId, null, null, null, null] });
        var json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!.AsObject();
        edit(json);
        await File.WriteAllTextAsync(SettingsPath, json.ToJsonString());
    }
}
```

Append these tests to `src/FourFoldAccountManager.Core.Tests/Panel/PanelLayoutPolicyTests.cs`, inside the class after the existing test:

```csharp
    [Fact]
    public void SettingsTransformsPreserveOverlayCardsAndClearAccountRemovesTheAccountsCards()
    {
        var accountId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var cards = new[]
        {
            new OverlayCardPlacement(OverlayAddOnKind.Xp, accountId, true, new OverlayBounds(0.1, 0.1, 0.2, 0.1)),
            new OverlayCardPlacement(OverlayAddOnKind.Xp, otherId, false, null)
        };
        var settings = PanelSettings.Default with
        {
            SlotAccountIds = [accountId, otherId, null, null, null],
            OverlayCards = cards
        };

        Assert.Equal(cards, PanelLayoutPolicy.WithLayout(settings, PanelLayout.OneByTwo).OverlayCards);
        Assert.Equal(cards, PanelLayoutPolicy.Assign(settings, 2, Guid.NewGuid()).OverlayCards);
        Assert.Equal(cards, PanelLayoutPolicy.WithSplitState(settings,
            new PanelSplitState("2x2.rows", [0.7, 0.3])).OverlayCards);
        Assert.Equal(cards, PanelLayoutPolicy.ResetSplitStates(settings).OverlayCards);
        Assert.Equal([cards[1]], PanelLayoutPolicy.ClearAccount(settings, accountId).OverlayCards);
    }
```

Append these tests to `src/FourFoldAccountManager.Core.Tests/Overlay/OverlayCardPolicyTests.cs`, inside the class:

```csharp
    [Fact]
    public void WithEnabledTogglesACardAndKeepsItsBounds()
    {
        var key = new OverlayCardKey(OverlayAddOnKind.Xp, Guid.NewGuid());
        var settings = PanelSettings.Default with
        {
            OverlayCards = [new OverlayCardPlacement(key.Kind, key.AccountId, true, First)]
        };

        var disabled = OverlayCardPolicy.WithEnabled(settings, key, false);
        var enabledAgain = OverlayCardPolicy.WithEnabled(disabled, key, true);

        Assert.Equal(new OverlayCardPlacement(key.Kind, key.AccountId, false, First), OverlayCardPolicy.Get(disabled, key));
        Assert.Equal(new OverlayCardPlacement(key.Kind, key.AccountId, true, First), OverlayCardPolicy.Get(enabledAgain, key));
    }

    [Fact]
    public void WithEnabledCreatesAPlacementWithoutBoundsForANewKey()
    {
        var key = new OverlayCardKey(OverlayAddOnKind.Xp, Guid.NewGuid());

        var settings = OverlayCardPolicy.WithEnabled(PanelSettings.Default, key, true);

        Assert.Equal([new OverlayCardPlacement(key.Kind, key.AccountId, true, null)], settings.OverlayCards);
    }

    [Fact]
    public void WithBoundsUpdatesOnlyTheMatchingCard()
    {
        var key = new OverlayCardKey(OverlayAddOnKind.Xp, Guid.NewGuid());
        var other = new OverlayCardPlacement(OverlayAddOnKind.Xp, Guid.NewGuid(), true, First);
        var settings = PanelSettings.Default with
        {
            OverlayCards = [other, new OverlayCardPlacement(key.Kind, key.AccountId, false, First)]
        };

        var moved = OverlayCardPolicy.WithBounds(settings, key, Second);

        Assert.Equal([other, new OverlayCardPlacement(key.Kind, key.AccountId, false, Second)], moved.OverlayCards);
    }

    [Fact]
    public void SettingsHelpersRejectInvalidKeysAndBounds()
    {
        Assert.Throws<ArgumentException>(() => OverlayCardPolicy.WithEnabled(
            PanelSettings.Default, new OverlayCardKey(OverlayAddOnKind.Xp, null), true));
        Assert.Throws<ArgumentException>(() => OverlayCardPolicy.WithBounds(
            PanelSettings.Default, new OverlayCardKey(OverlayAddOnKind.Xp, Guid.NewGuid()),
            new OverlayBounds(0.9, 0, 0.5, 0.5)));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: build FAILS with `'PanelSettings' does not contain a definition for 'OverlayCards'` and `'OverlayCardPolicy' does not contain a definition for 'WithEnabled'`.

- [ ] **Step 3: Update `PanelSettings`**

In `src/FourFoldAccountManager.Core/Models/PanelSettings.cs`, replace:

```csharp
    public IReadOnlyDictionary<Guid, OverlayBounds> XpOverlayBoundsByAccount { get; init; } =
        new Dictionary<Guid, OverlayBounds>();
```

with:

```csharp
    public IReadOnlyList<OverlayCardPlacement> OverlayCards { get; init; } = Array.Empty<OverlayCardPlacement>();

    // Migration input from settings written before overlay add-ons; validation converts it and never writes it back.
    [JsonPropertyName("xpOverlayBoundsByAccount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<Guid, OverlayBounds>? LegacyXpOverlayBoundsByAccount { get; init; }
```

The file already has `using System.Text.Json.Serialization;`.

- [ ] **Step 4: Add the settings-level helpers to `OverlayCardPolicy`**

In `src/FourFoldAccountManager.Core/Overlay/OverlayCardPolicy.cs`, add these members after `RemoveAccount`:

```csharp
    public static OverlayCardPlacement? Get(PanelSettings settings, OverlayCardKey key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.OverlayCards.FirstOrDefault(card => card.Key == key);
    }

    public static PanelSettings WithEnabled(PanelSettings settings, OverlayCardKey key, bool enabled) =>
        Upsert(settings, key, existing =>
            (existing ?? new OverlayCardPlacement(key.Kind, key.AccountId, false, null)) with { Enabled = enabled });

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
```

- [ ] **Step 5: Normalize overlay cards in `SettingsStore.Validate`**

In `src/FourFoldAccountManager.Core/Data/SettingsStore.cs`:

1. Add `using FourFoldAccountManager.Core.Overlay;` to the usings.
2. Delete this block:

```csharp
        if (settings.XpOverlayBoundsByAccount is null ||
            settings.XpOverlayBoundsByAccount.Any(entry =>
                entry.Key == Guid.Empty || entry.Value is null || !entry.Value.IsValid))
        {
            throw new InvalidDataException("Panel settings contain invalid XP overlay bounds.");
        }
```

3. Replace:

```csharp
        var overlayBounds = new Dictionary<Guid, OverlayBounds>(settings.XpOverlayBoundsByAccount);
```

with:

```csharp
        var overlayCards = OverlayCardPolicy.Normalize(settings.OverlayCards, settings.LegacyXpOverlayBoundsByAccount);
```

4. In the returned object initializer, replace `XpOverlayBoundsByAccount = overlayBounds` with `OverlayCards = overlayCards`. Do not copy `LegacyXpOverlayBoundsByAccount`. Leaving it null is what keeps it out of the saved file.

- [ ] **Step 6: Update `PanelLayoutPolicy`**

In `src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs`:

1. Add `using FourFoldAccountManager.Core.Overlay;`.
2. In `WithLayout`, `Assign`, and `CopySettings`, replace `XpOverlayBoundsByAccount = settings.XpOverlayBoundsByAccount` with `OverlayCards = settings.OverlayCards`.
3. In `ClearAccount`, delete these two lines:

```csharp
        var overlayBounds = new Dictionary<Guid, OverlayBounds>(settings.XpOverlayBoundsByAccount);
        overlayBounds.Remove(accountId);
```

   Then replace `XpOverlayBoundsByAccount = overlayBounds` with:

```csharp
            OverlayCards = OverlayCardPolicy.RemoveAccount(settings.OverlayCards, accountId)
```

4. Delete the methods `GetXpOverlayBounds` and `WithXpOverlayBounds` entirely.

Verify with: `grep -n "XpOverlayBoundsByAccount\|GetXpOverlayBounds\|WithXpOverlayBounds" -r src/FourFoldAccountManager.Core`
Expected: no output.

- [ ] **Step 7: Keep `MainWindow` compiling with the old XP layer (temporary; Task 5 replaces this code)**

In `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`:

1. Add `using FourFoldAccountManager.Core.Overlay;` after `using FourFoldAccountManager.Core.Models;`.
2. In `RefreshTrackerRows`, replace:

```csharp
            var overlayBounds = overlayAccountId is { } overlayId
                ? PanelLayoutPolicy.GetXpOverlayBounds(_panelSettings, overlayId)
                : null;
```

   with:

```csharp
            var overlayBounds = overlayAccountId is { } overlayId &&
                OverlayCardPolicy.Get(_panelSettings, new OverlayCardKey(OverlayAddOnKind.Xp, overlayId)) is
                    { Enabled: true, Bounds: { } savedBounds }
                ? savedBounds
                : null;
```

3. In `SaveXpOverlayBoundsAsync`, replace:

```csharp
                PanelLayoutPolicy.WithXpOverlayBounds(settings, accountId, bounds));
```

   with:

```csharp
                OverlayCardPolicy.WithEnabled(
                    OverlayCardPolicy.WithBounds(settings, new OverlayCardKey(OverlayAddOnKind.Xp, accountId), bounds),
                    new OverlayCardKey(OverlayAddOnKind.Xp, accountId),
                    true));
```

- [ ] **Step 8: Run the tests and build to verify they pass**

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: PASS, including the 4 `SettingsStoreOverlayTests`, the new `PanelLayoutPolicyTests` test, and the 4 new `OverlayCardPolicyTests`.

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 9: Commit**

```bash
git add -A src/FourFoldAccountManager.Core src/FourFoldAccountManager.Core.Tests src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs
git commit -m "feat: persist overlay cards and migrate XP overlay bounds"
```

---

### Task 3: Card frame and multi-card overlay layer (Desktop)

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Views/OverlayCardFrame.xaml`
- Create: `src/FourFoldAccountManager.Desktop/Views/OverlayCardFrame.xaml.cs`
- Create: `src/FourFoldAccountManager.Desktop/Views/OverlayCardLayer.cs`
- Create: `src/FourFoldAccountManager.Desktop/Views/XpOverlayCardData.cs`
- Create: `src/FourFoldAccountManager.Desktop/Resources/OverlayCardTemplates.xaml`
- Modify: `src/FourFoldAccountManager.Desktop/App.xaml`
- Create: `src/FourFoldAccountManager.Desktop.Tests/WpfTestHost.cs`
- Modify: `src/FourFoldAccountManager.Desktop.Tests/Views/ExperienceCalculatorPanelTests.cs`
- Test: `src/FourFoldAccountManager.Desktop.Tests/Views/OverlayCardLayerTests.cs`

**Interfaces:**
- Consumes (Tasks 1-2): `OverlayBounds`, `OverlayCardKey`, `OverlayAddOnKind`, `OverlayAddOnDefinition`, `OverlayAddOnCatalog.All`/`IndexOf`, `OverlayCardPolicy.DefaultBounds`.
- Produces:
  - `OverlayCardFrame : UserControl` with `object? CardData`, `bool IsEditing`, internal thumbs `MoveThumb` and `ResizeThumb`, and the events:
    - `CardDragStarted` (`EventHandler`)
    - `MoveDelta`, `ResizeDelta` (`DragDeltaEventHandler`)
    - `MoveCompleted`, `ResizeCompleted` (`DragCompletedEventHandler`)
  - `record OverlayCardModel(OverlayCardKey Key, OverlayAddOnDefinition Definition, OverlayBounds? Bounds, int CascadeIndex, object Data)`
  - `OverlayCardLayer : Canvas` with:
    - `void SetCards(IReadOnlyList<OverlayCardModel> cards, bool editing)`
    - `void RestoreSavedBounds()`
    - `event EventHandler<OverlayCardBoundsCommittedEventArgs>? BoundsCommitted`
    - `internal IReadOnlyDictionary<OverlayCardKey, OverlayCardFrame> Frames`
  - `class OverlayCardBoundsCommittedEventArgs(OverlayCardKey key, OverlayBounds bounds)` with `Key` and `Bounds`.
  - `record XpOverlayCardData(string AccountLabel, string XpPerHourText)`
  - Test helper: `static class WpfTestHost { static void Run(Action body); }` (in `FourFoldAccountManager.Desktop.Tests`).

- [ ] **Step 1: Add the shared WPF test host and move the existing STA test onto it**

WPF allows one `Application` per process, and its resources belong to the thread that created it. All WPF tests therefore run on one shared STA dispatcher.

Create `src/FourFoldAccountManager.Desktop.Tests/WpfTestHost.cs`:

```csharp
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace FourFoldAccountManager.Desktop.Tests;

// WPF allows one Application per process and ties its resources to one thread,
// so every UI test runs on this shared STA dispatcher with the app's resources loaded.
internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> SharedDispatcher = new(StartDispatcher);

    public static void Run(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ExceptionDispatchInfo? failure = null;
        SharedDispatcher.Value.Invoke(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        failure?.Throw();
    }

    private static Dispatcher StartDispatcher()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        var thread = new Thread(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var resource in new[] { "Resources/Theme.xaml", "Resources/OverlayCardTemplates.xaml" })
            {
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/FourFoldAccountManager.Desktop;component/{resource}", UriKind.Relative)
                });
            }

            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
```

In `src/FourFoldAccountManager.Desktop.Tests/Views/ExperienceCalculatorPanelTests.cs`, replace the whole body of `TargetIsManualAndSurvivesOnlySameAccountRefresh` with the same assertions wrapped in `WpfTestHost.Run`. The test no longer creates its own thread or `Application`:

```csharp
    [Fact]
    public void TargetIsManualAndSurvivesOnlySameAccountRefresh() => WpfTestHost.Run(() =>
    {
        var panel = new ExperienceCalculatorPanel();
        var first = AccountProfile.Create("First");
        var second = AccountProfile.Create("Second");
        panel.SetAccounts([first, second]);

        panel.SetSnapshot(first, Snapshot(5));
        Assert.Equal(string.Empty, panel.TargetLevelBox.Text);
        Assert.Equal("—", panel.RemainingText.Text);

        panel.TargetLevelBox.Text = "3";
        Assert.Equal("25", panel.RemainingText.Text);

        panel.SetSnapshot(first, Snapshot(10));
        Assert.Equal("3", panel.TargetLevelBox.Text);
        Assert.Equal("20", panel.RemainingText.Text);

        panel.TargetLevelBox.Clear();
        Assert.Equal("—", panel.RemainingText.Text);

        panel.TargetLevelBox.Text = "3";
        panel.SetSnapshot(second, Snapshot(5));
        Assert.Equal(string.Empty, panel.TargetLevelBox.Text);
        Assert.Equal("—", panel.RemainingText.Text);
    });
```

Remove the now-unused `using System.Runtime.ExceptionServices;` and `using System.Threading;` from that file. Keep `using System.Windows;` only if the compiler reports it is still needed; otherwise remove it too.

- [ ] **Step 2: Write the failing layer tests**

Create `src/FourFoldAccountManager.Desktop.Tests/Views/OverlayCardLayerTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class OverlayCardLayerTests
{
    private static readonly OverlayAddOnDefinition Xp = OverlayAddOnCatalog.All[0];

    [Fact]
    public void SetCardsAddsUpdatesAndRemovesFramesByKey() => WpfTestHost.Run(() =>
    {
        var layer = MeasuredLayer(1000, 500);
        var first = NewKey();
        var second = NewKey();

        layer.SetCards([Model(first, new OverlayBounds(0.1, 0.1, 0.2, 0.1), "A"), Model(second, null, "B")], editing: false);

        Assert.Equal(2, layer.Children.Count);
        var frame = layer.Frames[first];
        Assert.Equal(100, Canvas.GetLeft(frame), 3);
        Assert.Equal(50, Canvas.GetTop(frame), 3);
        Assert.Equal(200, frame.Width, 3);
        Assert.Equal(50, frame.Height, 3);
        Assert.False(frame.IsEditing);

        layer.SetCards([Model(first, new OverlayBounds(0.5, 0.5, 0.2, 0.1), "A2")], editing: true);

        Assert.Same(frame, Assert.Single(layer.Children.Cast<UIElement>()));
        Assert.Equal(500, Canvas.GetLeft(frame), 3);
        Assert.Equal(new XpOverlayCardData("A2", "1 XP/hr"), frame.CardData);
        Assert.True(frame.IsEditing);
    });

    [Fact]
    public void CardsWithoutBoundsAreCascadedFromTheDefaultPosition() => WpfTestHost.Run(() =>
    {
        var layer = MeasuredLayer(1000, 500);
        var first = NewKey();
        var second = NewKey();

        layer.SetCards([Model(first, null, "A", cascade: 0), Model(second, null, "B", cascade: 1)], editing: false);

        Assert.Equal(12, Canvas.GetLeft(layer.Frames[first]), 3);
        Assert.Equal(28, Canvas.GetLeft(layer.Frames[second]), 3);
        Assert.Equal(28, Canvas.GetTop(layer.Frames[second]), 3);
    });

    [Fact]
    public void CardEnabledBeforeTheLayerIsMeasuredAppearsAtTheDefaultOnceMeasured() => WpfTestHost.Run(() =>
    {
        var layer = new OverlayCardLayer();
        var key = NewKey();

        layer.SetCards([Model(key, null, "A")], editing: false);
        Arrange(layer, 1000, 500);

        Assert.Equal(12, Canvas.GetLeft(layer.Frames[key]), 3);
        Assert.Equal(12, Canvas.GetTop(layer.Frames[key]), 3);
    });

    [Fact]
    public void DraggingCommitsBoundsForTheDraggedCard() => WpfTestHost.Run(() =>
    {
        var layer = MeasuredLayer(1000, 500);
        var key = NewKey();
        layer.SetCards([Model(key, new OverlayBounds(0.1, 0.1, 0.2, 0.1), "A"), Model(NewKey(), null, "B")], editing: true);
        OverlayCardBoundsCommittedEventArgs? committed = null;
        layer.BoundsCommitted += (_, args) => committed = args;

        var frame = layer.Frames[key];
        frame.MoveThumb.RaiseEvent(new DragDeltaEventArgs(50, 25));
        frame.MoveThumb.RaiseEvent(new DragCompletedEventArgs(50, 25, false));

        Assert.NotNull(committed);
        Assert.Equal(key, committed.Key);
        Assert.Equal(0.15, committed.Bounds.X, 6);
        Assert.Equal(0.15, committed.Bounds.Y, 6);
        Assert.Equal(0.2, committed.Bounds.Width, 6);
    });

    [Fact]
    public void CanceledDragRestoresSavedBoundsWithoutCommitting() => WpfTestHost.Run(() =>
    {
        var layer = MeasuredLayer(1000, 500);
        var key = NewKey();
        layer.SetCards([Model(key, new OverlayBounds(0.1, 0.1, 0.2, 0.1), "A")], editing: true);
        var commits = 0;
        layer.BoundsCommitted += (_, _) => commits++;

        var frame = layer.Frames[key];
        frame.MoveThumb.RaiseEvent(new DragDeltaEventArgs(50, 25));
        frame.MoveThumb.RaiseEvent(new DragCompletedEventArgs(50, 25, true));

        Assert.Equal(0, commits);
        Assert.Equal(100, Canvas.GetLeft(frame), 3);
    });

    [Fact]
    public void CompletingADragAfterTheCardWasRemovedCommitsNothing() => WpfTestHost.Run(() =>
    {
        var layer = MeasuredLayer(1000, 500);
        var key = NewKey();
        layer.SetCards([Model(key, new OverlayBounds(0.1, 0.1, 0.2, 0.1), "A")], editing: true);
        var commits = 0;
        layer.BoundsCommitted += (_, _) => commits++;

        var frame = layer.Frames[key];
        frame.MoveThumb.RaiseEvent(new DragDeltaEventArgs(50, 25));
        layer.SetCards([], editing: false);
        frame.MoveThumb.RaiseEvent(new DragCompletedEventArgs(50, 25, false));

        Assert.Equal(0, commits);
        Assert.Empty(layer.Children);
    });

    [Fact]
    public void LayerTakesInputOnlyOnCardsAndOnlyInEditMode() => WpfTestHost.Run(() =>
    {
        var layer = MeasuredLayer(1000, 500);
        var key = NewKey();

        layer.SetCards([Model(key, new OverlayBounds(0.1, 0.1, 0.2, 0.1), "A")], editing: false);
        Assert.False(layer.IsHitTestVisible);
        Assert.False(layer.Frames[key].IsHitTestVisible);

        layer.SetCards([Model(key, new OverlayBounds(0.1, 0.1, 0.2, 0.1), "A")], editing: true);
        layer.UpdateLayout();
        Assert.True(layer.IsHitTestVisible);
        Assert.NotNull(VisualTreeHelper.HitTest(layer, new Point(150, 70)));
        Assert.Null(VisualTreeHelper.HitTest(layer, new Point(900, 450)));

        layer.SetCards([], editing: true);
        Assert.False(layer.IsHitTestVisible);
    });

    private static OverlayCardKey NewKey() => new(OverlayAddOnKind.Xp, Guid.NewGuid());

    private static OverlayCardModel Model(OverlayCardKey key, OverlayBounds? bounds, string label, int cascade = 0) =>
        new(key, Xp, bounds, cascade, new XpOverlayCardData(label, "1 XP/hr"));

    private static OverlayCardLayer MeasuredLayer(double width, double height)
    {
        var layer = new OverlayCardLayer();
        Arrange(layer, width, height);
        return layer;
    }

    private static void Arrange(FrameworkElement element, double width, double height)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release --filter "FullyQualifiedName~OverlayCardLayerTests"`
Expected: build FAILS with `The type or namespace name 'OverlayCardLayer' could not be found`.

- [ ] **Step 4: Create the XP card data and its template**

`src/FourFoldAccountManager.Desktop/Views/XpOverlayCardData.cs`:

```csharp
namespace FourFoldAccountManager.Desktop.Views;

public sealed record XpOverlayCardData(string AccountLabel, string XpPerHourText);
```

`src/FourFoldAccountManager.Desktop/Resources/OverlayCardTemplates.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:views="clr-namespace:FourFoldAccountManager.Desktop.Views">
    <!-- Each overlay add-on's card content, selected by the type of its card data. -->
    <DataTemplate DataType="{x:Type views:XpOverlayCardData}">
        <Grid VerticalAlignment="Center">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>
            <TextBlock Text="{Binding AccountLabel}" FontSize="10" FontWeight="SemiBold"
                       Foreground="{DynamicResource Brush.TextSecondary}" TextTrimming="CharacterEllipsis"
                       ToolTip="{Binding AccountLabel}" />
            <TextBlock Grid.Row="1" Text="{Binding XpPerHourText}" FontSize="14" FontWeight="Bold"
                       Foreground="{DynamicResource Brush.AccentGold}" TextTrimming="CharacterEllipsis" />
        </Grid>
    </DataTemplate>
</ResourceDictionary>
```

In `src/FourFoldAccountManager.Desktop/App.xaml`, add the dictionary after `Theme.xaml`:

```xml
                <ResourceDictionary Source="Resources/Theme.xaml" />
                <ResourceDictionary Source="Resources/OverlayCardTemplates.xaml" />
```

- [ ] **Step 5: Create the card frame**

`src/FourFoldAccountManager.Desktop/Views/OverlayCardFrame.xaml` is the chrome from `XpOverlayCard.xaml`, with its content replaced by a `ContentPresenter`:

```xml
<UserControl x:Class="FourFoldAccountManager.Desktop.Views.OverlayCardFrame"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Focusable="False" IsHitTestVisible="False" SnapsToDevicePixels="True">
    <Grid>
        <Border x:Name="CardChrome" Padding="9,5" CornerRadius="8"
                Background="#E6121920" BorderThickness="1">
            <Border.Style>
                <Style TargetType="Border">
                    <Setter Property="BorderBrush" Value="{DynamicResource Brush.BorderStrong}" />
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding IsEditing, RelativeSource={RelativeSource AncestorType={x:Type UserControl}}}"
                                     Value="True">
                            <Setter Property="BorderBrush" Value="{DynamicResource Brush.AccentGold}" />
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </Border.Style>
            <ContentPresenter Content="{Binding CardData, RelativeSource={RelativeSource AncestorType={x:Type UserControl}}}"
                              VerticalAlignment="Center" />
        </Border>
        <Thumb x:Name="MoveThumb" Background="Transparent" Cursor="SizeAll"
               IsEnabled="{Binding IsEditing, RelativeSource={RelativeSource AncestorType={x:Type UserControl}}}"
               IsHitTestVisible="{Binding IsEditing, RelativeSource={RelativeSource AncestorType={x:Type UserControl}}}">
            <Thumb.Template>
                <ControlTemplate TargetType="Thumb">
                    <Border Background="{TemplateBinding Background}" />
                </ControlTemplate>
            </Thumb.Template>
        </Thumb>
        <Thumb x:Name="ResizeThumb" Width="14" Height="14" Margin="4"
               HorizontalAlignment="Right" VerticalAlignment="Bottom" Cursor="SizeNWSE"
               Background="{DynamicResource Brush.AccentGold}"
               IsEnabled="{Binding IsEditing, RelativeSource={RelativeSource AncestorType={x:Type UserControl}}}"
               AutomationProperties.Name="Resize overlay card">
            <Thumb.Style>
                <Style TargetType="Thumb">
                    <Setter Property="Visibility" Value="Collapsed" />
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding IsEditing, RelativeSource={RelativeSource AncestorType={x:Type UserControl}}}"
                                     Value="True">
                            <Setter Property="Visibility" Value="Visible" />
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </Thumb.Style>
            <Thumb.Template>
                <ControlTemplate TargetType="Thumb">
                    <Border Background="{TemplateBinding Background}" CornerRadius="3" />
                </ControlTemplate>
            </Thumb.Template>
        </Thumb>
    </Grid>
</UserControl>
```

`src/FourFoldAccountManager.Desktop/Views/OverlayCardFrame.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FourFoldAccountManager.Desktop.Views;

public partial class OverlayCardFrame : UserControl
{
    public static readonly DependencyProperty CardDataProperty = DependencyProperty.Register(
        nameof(CardData), typeof(object), typeof(OverlayCardFrame), new PropertyMetadata(null));

    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing), typeof(bool), typeof(OverlayCardFrame), new PropertyMetadata(false, OnIsEditingChanged));

    public OverlayCardFrame()
    {
        InitializeComponent();
        MoveThumb.DragStarted += (_, _) => CardDragStarted?.Invoke(this, EventArgs.Empty);
        MoveThumb.DragDelta += (_, args) => MoveDelta?.Invoke(this, args);
        MoveThumb.DragCompleted += (_, args) => MoveCompleted?.Invoke(this, args);
        ResizeThumb.DragStarted += (_, _) => CardDragStarted?.Invoke(this, EventArgs.Empty);
        ResizeThumb.DragDelta += (_, args) => ResizeDelta?.Invoke(this, args);
        ResizeThumb.DragCompleted += (_, args) => ResizeCompleted?.Invoke(this, args);
    }

    public event EventHandler? CardDragStarted;

    public event DragDeltaEventHandler? MoveDelta;

    public event DragCompletedEventHandler? MoveCompleted;

    public event DragDeltaEventHandler? ResizeDelta;

    public event DragCompletedEventHandler? ResizeCompleted;

    public object? CardData
    {
        get => GetValue(CardDataProperty);
        set => SetValue(CardDataProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    private static void OnIsEditingChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is OverlayCardFrame frame)
        {
            frame.IsHitTestVisible = (bool)args.NewValue;
        }
    }
}
```

- [ ] **Step 6: Create the layer**

`src/FourFoldAccountManager.Desktop/Views/OverlayCardLayer.cs`. It reuses `XpOverlayLayer`'s geometry logic, applied per card:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record OverlayCardModel(
    OverlayCardKey Key,
    OverlayAddOnDefinition Definition,
    OverlayBounds? Bounds,
    int CascadeIndex,
    object Data);

public sealed class OverlayCardBoundsCommittedEventArgs(OverlayCardKey key, OverlayBounds bounds) : EventArgs
{
    public OverlayCardKey Key { get; } = key;

    public OverlayBounds Bounds { get; } = bounds;
}

public sealed class OverlayCardLayer : Canvas
{
    private const int DraggingZIndex = 1000;

    private readonly Dictionary<OverlayCardKey, CardEntry> _cards = [];

    public OverlayCardLayer()
    {
        // No background: empty layer space never takes input, so a window-wide layer
        // cannot block the per-client layers or the game underneath it.
        Background = null;
        ClipToBounds = true;
        IsHitTestVisible = false;
        SizeChanged += (_, _) => ApplyAllBounds();
    }

    public event EventHandler<OverlayCardBoundsCommittedEventArgs>? BoundsCommitted;

    internal IReadOnlyDictionary<OverlayCardKey, OverlayCardFrame> Frames =>
        _cards.ToDictionary(entry => entry.Key, entry => entry.Value.Frame);

    public void SetCards(IReadOnlyList<OverlayCardModel> cards, bool editing)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var incoming = cards.ToDictionary(card => card.Key);
        foreach (var key in _cards.Keys.Where(key => !incoming.ContainsKey(key)).ToArray())
        {
            RemoveCard(key);
        }

        foreach (var model in cards)
        {
            var isNew = !_cards.TryGetValue(model.Key, out var entry);
            if (isNew)
            {
                entry = AddCard(model);
            }

            var boundsChanged = isNew || entry!.Model.Bounds != model.Bounds ||
                entry.Model.CascadeIndex != model.CascadeIndex;
            entry!.Model = model;
            entry.Frame.CardData = model.Data;
            entry.Frame.IsEditing = editing;
            Panel.SetZIndex(entry.Frame, OverlayAddOnCatalog.IndexOf(model.Key.Kind));
            if (boundsChanged)
            {
                ApplyBounds(entry);
            }
        }

        IsHitTestVisible = editing && _cards.Count > 0;
    }

    public void RestoreSavedBounds() => ApplyAllBounds();

    private CardEntry AddCard(OverlayCardModel model)
    {
        var frame = new OverlayCardFrame { Tag = model.Key };
        frame.CardDragStarted += OnCardDragStarted;
        frame.MoveDelta += OnMoveDelta;
        frame.MoveCompleted += OnManipulationCompleted;
        frame.ResizeDelta += OnResizeDelta;
        frame.ResizeCompleted += OnManipulationCompleted;
        Children.Add(frame);
        var entry = new CardEntry(frame, model);
        _cards.Add(model.Key, entry);
        return entry;
    }

    private void RemoveCard(OverlayCardKey key)
    {
        if (!_cards.Remove(key, out var entry))
        {
            return;
        }

        entry.Frame.CardDragStarted -= OnCardDragStarted;
        entry.Frame.MoveDelta -= OnMoveDelta;
        entry.Frame.MoveCompleted -= OnManipulationCompleted;
        entry.Frame.ResizeDelta -= OnResizeDelta;
        entry.Frame.ResizeCompleted -= OnManipulationCompleted;
        Children.Remove(entry.Frame);
    }

    private void ApplyAllBounds()
    {
        foreach (var entry in _cards.Values)
        {
            ApplyBounds(entry);
        }
    }

    private void ApplyBounds(CardEntry entry)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var model = entry.Model;
        var bounds = model.Bounds?.ClampToViewport() ??
            OverlayCardPolicy.DefaultBounds(model.Definition, ActualWidth, ActualHeight, model.CascadeIndex);
        var (minimumWidth, minimumHeight) = MinimumSize(entry);
        entry.Frame.MinWidth = minimumWidth;
        entry.Frame.MinHeight = minimumHeight;
        var width = Math.Clamp(bounds.Width * ActualWidth, minimumWidth, ActualWidth);
        var height = Math.Clamp(bounds.Height * ActualHeight, minimumHeight, ActualHeight);
        var left = Math.Clamp(bounds.X * ActualWidth, 0d, ActualWidth - width);
        var top = Math.Clamp(bounds.Y * ActualHeight, 0d, ActualHeight - height);
        SetGeometry(entry.Frame, left, top, width, height);
    }

    private (double Width, double Height) MinimumSize(CardEntry entry) =>
        (Math.Min(entry.Model.Definition.MinimumWidth, ActualWidth),
         Math.Min(entry.Model.Definition.MinimumHeight, ActualHeight));

    private static void SetGeometry(OverlayCardFrame frame, double left, double top, double width, double height)
    {
        frame.Width = width;
        frame.Height = height;
        SetLeft(frame, left);
        SetTop(frame, top);
    }

    private void OnCardDragStarted(object? sender, EventArgs args)
    {
        if (sender is OverlayCardFrame frame && TryGetEntry(frame, out _))
        {
            Panel.SetZIndex(frame, DraggingZIndex);
        }
    }

    private void OnMoveDelta(object sender, DragDeltaEventArgs args)
    {
        if (sender is not OverlayCardFrame frame || !TryGetEntry(frame, out _) ||
            ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var left = Math.Clamp(GetLeft(frame) + args.HorizontalChange, 0d, ActualWidth - frame.Width);
        var top = Math.Clamp(GetTop(frame) + args.VerticalChange, 0d, ActualHeight - frame.Height);
        SetGeometry(frame, left, top, frame.Width, frame.Height);
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs args)
    {
        if (sender is not OverlayCardFrame frame || !TryGetEntry(frame, out var entry) ||
            ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var (minimumWidth, minimumHeight) = MinimumSize(entry);
        var width = Math.Clamp(frame.Width + args.HorizontalChange, minimumWidth, ActualWidth);
        var height = Math.Clamp(frame.Height + args.VerticalChange, minimumHeight, ActualHeight);
        var left = Math.Clamp(GetLeft(frame), 0d, ActualWidth - width);
        var top = Math.Clamp(GetTop(frame), 0d, ActualHeight - height);
        SetGeometry(frame, left, top, width, height);
    }

    private void OnManipulationCompleted(object sender, DragCompletedEventArgs args)
    {
        // A card removed mid-drag (full screen exited, card switched off) must not save bounds.
        if (sender is not OverlayCardFrame frame || !TryGetEntry(frame, out var entry))
        {
            return;
        }

        Panel.SetZIndex(frame, OverlayAddOnCatalog.IndexOf(entry.Model.Key.Kind));
        if (args.Canceled)
        {
            ApplyBounds(entry);
            return;
        }

        if (GetVisualBounds(entry) is { } bounds)
        {
            BoundsCommitted?.Invoke(this, new OverlayCardBoundsCommittedEventArgs(entry.Model.Key, bounds));
        }
    }

    private OverlayBounds? GetVisualBounds(CardEntry entry)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return null;
        }

        var frame = entry.Frame;
        var (minimumWidth, minimumHeight) = MinimumSize(entry);
        var width = Math.Clamp(frame.Width, minimumWidth, ActualWidth);
        var height = Math.Clamp(frame.Height, minimumHeight, ActualHeight);
        var left = Math.Clamp(GetLeft(frame), 0d, ActualWidth - width);
        var top = Math.Clamp(GetTop(frame), 0d, ActualHeight - height);
        return new OverlayBounds(left / ActualWidth, top / ActualHeight, width / ActualWidth, height / ActualHeight);
    }

    private bool TryGetEntry(OverlayCardFrame frame, out CardEntry entry)
    {
        if (frame.Tag is OverlayCardKey key && _cards.TryGetValue(key, out var candidate) &&
            ReferenceEquals(candidate.Frame, frame))
        {
            entry = candidate;
            return true;
        }

        entry = null!;
        return false;
    }

    private sealed class CardEntry(OverlayCardFrame frame, OverlayCardModel model)
    {
        public OverlayCardFrame Frame { get; } = frame;

        public OverlayCardModel Model { get; set; } = model;
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`
Expected: PASS. All Desktop tests pass, including the 7 `OverlayCardLayerTests` and the migrated `ExperienceCalculatorPanelTests`.

If `DraggingCommitsBoundsForTheDraggedCard` fails with a zero delta, the thumb delta was not routed. Confirm that `MoveThumb` is the `x:Name` in `OverlayCardFrame.xaml`, and that the frame subscribes in its constructor after `InitializeComponent()`.

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add -A src/FourFoldAccountManager.Desktop src/FourFoldAccountManager.Desktop.Tests
git commit -m "feat: add reusable overlay card frame and layer"
```

---

### Task 4: Overlays panel with on/off switches (Desktop)

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Views/FullscreenOverlayTray.xaml`
- Create: `src/FourFoldAccountManager.Desktop/Views/FullscreenOverlayTray.xaml.cs`
- Test: `src/FourFoldAccountManager.Desktop.Tests/Views/FullscreenOverlayTrayTests.cs`

The old `FullscreenXpOverlayTray` stays in place until Task 5 switches `MainWindow` over and deletes it.

**Interfaces:**
- Consumes: `OverlayCardKey` (Task 1), `FullscreenXpOverlayTabVisibilityState` (existing, unchanged), `WpfTestHost` (Task 3).
- Produces:
  - `record OverlayTraySwitch(OverlayCardKey Key, string Label, string Detail, bool IsEnabled)`
  - `record OverlayTrayAccountRow(Guid AccountId, string AccountLabel, IReadOnlyList<OverlayTraySwitch> Switches)`
  - `class OverlayCardToggleRequestedEventArgs(OverlayCardKey key, bool enabled)` with `Key` and `Enabled`.
  - `FullscreenOverlayTray : UserControl` with:
    - `SetRows(IReadOnlyList<OverlayTraySwitch> globalSwitches, IReadOnlyList<OverlayTrayAccountRow> accountRows)`
    - `SetFullscreen(bool)`, `SetEditing(bool)`, `RevealEdgeTab()`, `DismissEdgeTab()`
    - events `EditRequested`, `DoneRequested` (`EventHandler`) and `CardToggleRequested` (`EventHandler<OverlayCardToggleRequestedEventArgs>`)
    - `internal void RequestToggle(OverlayCardKey key, bool enabled)`
    - internal named elements `GlobalSection`, `GlobalSwitchesControl`, `AccountRowsControl`

- [ ] **Step 1: Write the failing tests**

Create `src/FourFoldAccountManager.Desktop.Tests/Views/FullscreenOverlayTrayTests.cs`:

```csharp
using System.Windows;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class FullscreenOverlayTrayTests
{
    [Fact]
    public void GlobalSectionIsShownOnlyWhenGlobalSwitchesExist() => WpfTestHost.Run(() =>
    {
        var tray = new FullscreenOverlayTray();

        tray.SetRows([], [Row("Alice")]);
        Assert.Equal(Visibility.Collapsed, tray.GlobalSection.Visibility);

        tray.SetRows([new OverlayTraySwitch(new OverlayCardKey(OverlayAddOnKind.Xp, null), "Test", "", false)], []);
        Assert.Equal(Visibility.Visible, tray.GlobalSection.Visibility);
        Assert.Single(tray.GlobalSwitchesControl.Items);
    });

    [Fact]
    public void RowsAreReplacedOnEverySetRowsCall() => WpfTestHost.Run(() =>
    {
        var tray = new FullscreenOverlayTray();
        var first = Row("Alice", enabled: true);

        tray.SetRows([], [first, Row("Bob")]);
        Assert.Equal(2, tray.AccountRowsControl.Items.Count);

        var reverted = first with { Switches = [first.Switches[0] with { IsEnabled = false }] };
        tray.SetRows([], [reverted]);
        Assert.Equal(reverted, Assert.Single(tray.AccountRowsControl.Items.Cast<OverlayTrayAccountRow>()));
    });

    [Fact]
    public void ToggleRequestsAreRaisedOnlyWhileEditing() => WpfTestHost.Run(() =>
    {
        var tray = new FullscreenOverlayTray();
        var key = new OverlayCardKey(OverlayAddOnKind.Xp, Guid.NewGuid());
        OverlayCardToggleRequestedEventArgs? requested = null;
        tray.CardToggleRequested += (_, args) => requested = args;
        tray.SetFullscreen(true);

        tray.RequestToggle(key, true);
        Assert.Null(requested);

        tray.SetEditing(true);
        tray.RequestToggle(key, true);
        Assert.NotNull(requested);
        Assert.Equal(key, requested.Key);
        Assert.True(requested.Enabled);
    });

    private static OverlayTrayAccountRow Row(string label, bool enabled = false)
    {
        var accountId = Guid.NewGuid();
        return new OverlayTrayAccountRow(accountId, label,
            [new OverlayTraySwitch(new OverlayCardKey(OverlayAddOnKind.Xp, accountId), "XP/hr", "1 XP/hr", enabled)]);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release --filter "FullyQualifiedName~FullscreenOverlayTrayTests"`
Expected: build FAILS with `The type or namespace name 'FullscreenOverlayTray' could not be found`.

- [ ] **Step 3: Create the tray XAML**

`src/FourFoldAccountManager.Desktop/Views/FullscreenOverlayTray.xaml` keeps the old tray's frame and edge tab, and replaces the drag list with switches:

```xml
<UserControl x:Class="FourFoldAccountManager.Desktop.Views.FullscreenOverlayTray"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Width="326" Height="560" Visibility="Collapsed" Focusable="False">
    <UserControl.Resources>
        <DataTemplate x:Key="OverlaySwitchTemplate">
            <Grid Margin="0,4,0,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <CheckBox Content="{Binding Label}" IsChecked="{Binding IsEnabled, Mode=OneWay}"
                          Click="Switch_Click" VerticalAlignment="Center" FontSize="11"
                          Foreground="{DynamicResource Brush.TextPrimary}"
                          AutomationProperties.Name="{Binding Label}" />
                <TextBlock Grid.Column="1" Text="{Binding Detail}" FontSize="10" FontWeight="SemiBold"
                           Foreground="{DynamicResource Brush.AccentGold}" Margin="8,0,0,0"
                           VerticalAlignment="Center" />
            </Grid>
        </DataTemplate>
    </UserControl.Resources>
    <Grid x:Name="TrayRoot" Width="326" Height="560" HorizontalAlignment="Right" VerticalAlignment="Center">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="26" />
        </Grid.ColumnDefinitions>
        <Border x:Name="TrayPanel" Grid.Column="0" Width="286" MaxHeight="520" Margin="0,0,10,0"
                HorizontalAlignment="Right" VerticalAlignment="Center" Padding="14" CornerRadius="12"
                Background="{DynamicResource Brush.Surface}" BorderBrush="{DynamicResource Brush.BorderStrong}"
                BorderThickness="1" Visibility="Collapsed" IsHitTestVisible="True">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>
                <Grid Margin="0,0,0,10">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment="Center">
                        <TextBlock Text="Overlays" FontSize="14" FontWeight="SemiBold" />
                        <TextBlock Text="Switch cards on, then drag to place them" FontSize="10"
                                   Foreground="{DynamicResource Brush.TextMuted}" Margin="0,3,0,0" />
                    </StackPanel>
                    <Button x:Name="DoneButton" Grid.Column="1" Content="Done" Click="DoneButton_Click"
                            Style="{StaticResource AppButtonStyle}" Height="30" Padding="10,0"
                            VerticalAlignment="Center" />
                </Grid>
                <ScrollViewer Grid.Row="1" MaxHeight="420" VerticalScrollBarVisibility="Auto"
                              HorizontalScrollBarVisibility="Disabled">
                    <StackPanel>
                        <Border x:Name="GlobalSection" Padding="10,8" Margin="0,3" CornerRadius="8"
                                Background="{DynamicResource Brush.ClientBackground}"
                                BorderBrush="{DynamicResource Brush.Border}" BorderThickness="1"
                                Visibility="Collapsed">
                            <StackPanel>
                                <TextBlock Text="Global" FontSize="12" FontWeight="SemiBold" />
                                <ItemsControl x:Name="GlobalSwitchesControl"
                                              ItemTemplate="{StaticResource OverlaySwitchTemplate}" />
                            </StackPanel>
                        </Border>
                        <ItemsControl x:Name="AccountRowsControl">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border Padding="10,8" Margin="0,3" CornerRadius="8"
                                            Background="{DynamicResource Brush.ClientBackground}"
                                            BorderBrush="{DynamicResource Brush.Border}" BorderThickness="1">
                                        <StackPanel>
                                            <TextBlock Text="{Binding AccountLabel}" FontSize="12" FontWeight="SemiBold"
                                                       TextTrimming="CharacterEllipsis" ToolTip="{Binding AccountLabel}" />
                                            <ItemsControl ItemsSource="{Binding Switches}"
                                                          ItemTemplate="{StaticResource OverlaySwitchTemplate}" />
                                        </StackPanel>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                </ScrollViewer>
            </Grid>
        </Border>
        <Button x:Name="EdgeTab" Grid.Column="1" Width="26" Height="76" Padding="0"
                HorizontalAlignment="Right" VerticalAlignment="Center" Click="EdgeTab_Click"
                MouseEnter="EdgeTab_MouseEnter" MouseLeave="EdgeTab_MouseLeave"
                Style="{StaticResource AppButtonStyle}"
                ToolTip="Edit fullscreen overlays" AutomationProperties.Name="Edit overlays">
            <Path Data="M 8,2 L 3,7 L 8,12" Stroke="{DynamicResource Brush.AccentGold}"
                  StrokeThickness="2" StrokeStartLineCap="Round" StrokeEndLineCap="Round"
                  Stretch="Uniform" Width="10" Height="16" />
        </Button>
    </Grid>
</UserControl>
```

Before relying on `Brush.TextPrimary`, confirm it exists: `grep -n "Brush.TextPrimary" src/FourFoldAccountManager.Desktop/Resources/Theme.xaml`. If there is no match, remove the `Foreground` attribute from the `CheckBox`. It is a `DynamicResource`, so a missing key would not throw, but it would render the default colour.

- [ ] **Step 4: Create the tray code-behind**

`src/FourFoldAccountManager.Desktop/Views/FullscreenOverlayTray.xaml.cs` copies the edge-tab behaviour from `FullscreenXpOverlayTray.xaml.cs` unchanged and replaces the drag code with switches:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record OverlayTraySwitch(OverlayCardKey Key, string Label, string Detail, bool IsEnabled);

public sealed record OverlayTrayAccountRow(
    Guid AccountId,
    string AccountLabel,
    IReadOnlyList<OverlayTraySwitch> Switches);

public sealed class OverlayCardToggleRequestedEventArgs(OverlayCardKey key, bool enabled) : EventArgs
{
    public OverlayCardKey Key { get; } = key;

    public bool Enabled { get; } = enabled;
}

public partial class FullscreenOverlayTray : UserControl
{
    private readonly FullscreenXpOverlayTabVisibilityState _tabVisibilityState = new();
    private bool _isFullScreen;
    private bool _isEditing;
    private bool _ignoreEdgeTabMouseEnterUntilLeave;

    public FullscreenOverlayTray()
    {
        InitializeComponent();
    }

    public event EventHandler? EditRequested;

    public event EventHandler? DoneRequested;

    public event EventHandler<OverlayCardToggleRequestedEventArgs>? CardToggleRequested;

    // Rows are rebuilt from saved settings after every toggle, so a failed save shows the persisted state.
    public void SetRows(IReadOnlyList<OverlayTraySwitch> globalSwitches, IReadOnlyList<OverlayTrayAccountRow> accountRows)
    {
        ArgumentNullException.ThrowIfNull(globalSwitches);
        ArgumentNullException.ThrowIfNull(accountRows);
        GlobalSwitchesControl.ItemsSource = globalSwitches.ToArray();
        GlobalSection.Visibility = globalSwitches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AccountRowsControl.ItemsSource = accountRows.ToArray();
    }

    public void SetFullscreen(bool isFullScreen)
    {
        _isFullScreen = isFullScreen;
        if (!isFullScreen)
        {
            _isEditing = false;
            TrayPanel.Visibility = Visibility.Collapsed;
        }

        Visibility = isFullScreen ? Visibility.Visible : Visibility.Collapsed;
        UpdateEdgeTabVisibility();
    }

    public void SetEditing(bool isEditing)
    {
        _isEditing = _isFullScreen && isEditing;
        TrayPanel.Visibility = _isEditing ? Visibility.Visible : Visibility.Collapsed;
    }

    public void DismissEdgeTab()
    {
        _tabVisibilityState.Dismiss();
        SetEditing(false);
        UpdateEdgeTabVisibility();
    }

    public void RevealEdgeTab()
    {
        var pointerWasAlreadyOverTab = _isFullScreen &&
            !_tabVisibilityState.IsVisible(_isFullScreen) && IsPointerOverEdgeTab();
        _tabVisibilityState.Reveal();
        _ignoreEdgeTabMouseEnterUntilLeave = pointerWasAlreadyOverTab;
        UpdateEdgeTabVisibility();
    }

    internal void RequestToggle(OverlayCardKey key, bool enabled)
    {
        if (!_isEditing)
        {
            return;
        }

        CardToggleRequested?.Invoke(this, new OverlayCardToggleRequestedEventArgs(key, enabled));
    }

    private void Switch_Click(object sender, RoutedEventArgs args)
    {
        if (sender is CheckBox { DataContext: OverlayTraySwitch item } checkBox)
        {
            RequestToggle(item.Key, checkBox.IsChecked == true);
        }
    }

    private void EdgeTab_Click(object sender, RoutedEventArgs args) => RequestEdit();

    private void EdgeTab_MouseEnter(object sender, MouseEventArgs args)
    {
        if (!_ignoreEdgeTabMouseEnterUntilLeave)
        {
            RequestEdit();
        }
    }

    private void EdgeTab_MouseLeave(object sender, MouseEventArgs args) =>
        _ignoreEdgeTabMouseEnterUntilLeave = false;

    private void DoneButton_Click(object sender, RoutedEventArgs args)
    {
        DismissEdgeTab();
        DoneRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateEdgeTabVisibility() =>
        EdgeTab.Visibility = _tabVisibilityState.IsVisible(_isFullScreen)
            ? Visibility.Visible
            : Visibility.Collapsed;

    private bool IsPointerOverEdgeTab()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return false;
        }

        var pointer = Mouse.GetPosition(this);
        var left = ActualWidth - EdgeTab.Width;
        var top = (ActualHeight - EdgeTab.Height) / 2d;
        return pointer.X >= left && pointer.X <= ActualWidth &&
            pointer.Y >= top && pointer.Y <= top + EdgeTab.Height;
    }

    private void RequestEdit()
    {
        if (!_isFullScreen || _isEditing)
        {
            return;
        }

        SetEditing(true);
        EditRequested?.Invoke(this, EventArgs.Empty);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`
Expected: PASS, including the 3 `FullscreenOverlayTrayTests`.

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add -A src/FourFoldAccountManager.Desktop/Views src/FourFoldAccountManager.Desktop.Tests
git commit -m "feat: add overlays panel with per-card switches"
```

---

### Task 5: Wire the add-on overlay into the main window and remove the XP-only overlay

**Files:**
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml` (lines 209-210, the tray element)
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs` (constructor lines 85-86; `EnterFullScreen`/`ExitFullScreen`/`SetXpOverlayEditing` lines 994-1076; `RefreshTrackerRows` lines 1094-1142; overlay handlers lines 1151-1200; slot creation lines 2075-2077 and 2126-2130; `PanelSlotCard` lines 2765-2800)
- Modify: `src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml:72`
- Modify: `README.md:34`
- Delete: `src/FourFoldAccountManager.Desktop/Views/XpOverlayLayer.cs`, `Views/XpOverlayCard.xaml`, `Views/XpOverlayCard.xaml.cs`, `Views/XpOverlayAccountChoice.cs`, `Views/FullscreenXpOverlayTray.xaml`, `Views/FullscreenXpOverlayTray.xaml.cs`

**Interfaces:**
- Consumes:
  - Task 1-2: `OverlayAddOnCatalog`, `OverlayAddOnScope`, `OverlayAddOnDefinition`, `OverlayCardKey`, and `OverlayCardPolicy.Get`/`WithEnabled`/`WithBounds`/`IsValidKey`.
  - Task 3: `OverlayCardLayer`, `OverlayCardModel`, `OverlayCardBoundsCommittedEventArgs`, `XpOverlayCardData`.
  - Task 4: `FullscreenOverlayTray`, `OverlayTraySwitch`, `OverlayTrayAccountRow`, `OverlayCardToggleRequestedEventArgs`.
- Produces:
  - `MainWindow.CreateOverlayCardData(OverlayAddOnKind, string accountLabel, XpTrackerRow?)`, the single place later projects add card data for a new kind.
  - The XAML element `GlobalOverlayLayer`, which the Timer project will use.

- [ ] **Step 1: Swap the tray and add the window-wide layer in XAML**

In `src/FourFoldAccountManager.Desktop/MainWindow.xaml`, replace:

```xml
        <views:FullscreenXpOverlayTray x:Name="FullscreenXpOverlayTray" Grid.RowSpan="2" Panel.ZIndex="900"
                                       HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,4,0" />
```

with:

```xml
        <views:OverlayCardLayer x:Name="GlobalOverlayLayer" Grid.RowSpan="2" Panel.ZIndex="800" />
        <views:FullscreenOverlayTray x:Name="FullscreenOverlayTray" Grid.RowSpan="2" Panel.ZIndex="900"
                                     HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,4,0" />
```

- [ ] **Step 2: Rename the tray and editing flag references in `MainWindow.xaml.cs`**

Run from the repository root:

```bash
sed -i -E 's/\bFullscreenXpOverlayTray\b/FullscreenOverlayTray/g; s/\b_xpOverlayEditing\b/_overlayEditing/g; s/\bSetXpOverlayEditing\b/SetOverlayEditing/g' \
  src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs
```

Then, in the constructor after `FullscreenOverlayTray.DoneRequested += (_, _) => SetOverlayEditing(false);`, add:

```csharp
        FullscreenOverlayTray.CardToggleRequested += FullscreenOverlayTray_CardToggleRequested;
        GlobalOverlayLayer.BoundsCommitted += OverlayLayer_BoundsCommitted;
```

- [ ] **Step 3: Use `OverlayCardLayer` for each client slot**

In the slot-creation method, replace:

```csharp
        var xpOverlayLayer = new XpOverlayLayer();
        Panel.SetZIndex(xpOverlayLayer, 50);
        browserHost.Children.Add(xpOverlayLayer);
```

with:

```csharp
        var overlayLayer = new OverlayCardLayer();
        Panel.SetZIndex(overlayLayer, 50);
        browserHost.Children.Add(overlayLayer);
```

In the same method, pass `overlayLayer` instead of `xpOverlayLayer` to `new PanelSlotCard(...)`, and replace:

```csharp
        xpOverlayLayer.AccountDropped += XpOverlayLayer_AccountDropped;
        xpOverlayLayer.BoundsCommitted += XpOverlayLayer_BoundsCommitted;
```

with:

```csharp
        overlayLayer.BoundsCommitted += OverlayLayer_BoundsCommitted;
```

In `PanelSlotCard`, replace the constructor parameter `XpOverlayLayer xpOverlayLayer,` with `OverlayCardLayer overlayLayer,`. Replace the property `public XpOverlayLayer XpOverlayLayer { get; } = xpOverlayLayer;` with `public OverlayCardLayer OverlayLayer { get; } = overlayLayer;`.

- [ ] **Step 4: Replace `RefreshTrackerRows` and add the card-data helpers**

Replace the whole `RefreshTrackerRows` method with:

```csharp
    private void RefreshTrackerRows()
    {
        var states = _xpTracker.GetStates().ToDictionary(state => state.AccountId);
        var accountRows = new List<OverlayTrayAccountRow>();
        var editing = _isFullScreen && _overlayEditing;
        _xpTrackerRows.Clear();
        foreach (var slot in _slotCards.OrderBy(slot => slot.SlotIndex))
        {
            var assignedAccountId = _panelSettings.SlotAccountIds[slot.SlotIndex];
            var account = assignedAccountId is { } id
                ? _accounts.FirstOrDefault(profile => profile.Id == id)
                : null;
            var label = account?.Label ?? "Account";
            var isOpen = assignedAccountId is { } openAccountId &&
                _openAccountIds.Contains(openAccountId) && slot.View is not null;
            var cards = new List<OverlayCardModel>();

            if (isOpen && assignedAccountId is { } trackedAccountId)
            {
                XpTrackerRow? trackerRow = null;
                if (states.TryGetValue(trackedAccountId, out var state))
                {
                    trackerRow = XpTrackerRow.FromState(slot.SlotIndex + 1, label, state);
                    _xpTrackerRows.Add(trackerRow);
                }

                if (account is not null)
                {
                    var switches = new List<OverlayTraySwitch>();
                    foreach (var definition in OverlayAddOnCatalog.All.Where(
                                 definition => definition.Scope == OverlayAddOnScope.Account))
                    {
                        AddOverlayAddOn(definition, new OverlayCardKey(definition.Kind, trackedAccountId),
                            label, trackerRow, cards, switches);
                    }

                    accountRows.Add(new OverlayTrayAccountRow(trackedAccountId, label, switches));
                }
            }

            slot.OverlayLayer.SetCards(cards, editing);
        }

        var globalCards = new List<OverlayCardModel>();
        var globalSwitches = new List<OverlayTraySwitch>();
        foreach (var definition in OverlayAddOnCatalog.All.Where(
                     definition => definition.Scope == OverlayAddOnScope.Global))
        {
            AddOverlayAddOn(definition, new OverlayCardKey(definition.Kind, null),
                string.Empty, null, globalCards, globalSwitches);
        }

        GlobalOverlayLayer.SetCards(globalCards, editing);
        FullscreenOverlayTray.SetRows(globalSwitches, accountRows);
        UpdatePluginSidebarVisibility();
    }

    private void AddOverlayAddOn(
        OverlayAddOnDefinition definition,
        OverlayCardKey key,
        string accountLabel,
        XpTrackerRow? trackerRow,
        List<OverlayCardModel> cards,
        List<OverlayTraySwitch> switches)
    {
        if (CreateOverlayCardData(definition.Kind, accountLabel, trackerRow) is not { } data)
        {
            return;
        }

        var placement = OverlayCardPolicy.Get(_panelSettings, key);
        switches.Add(new OverlayTraySwitch(key, definition.DisplayName, DescribeOverlayCard(data),
            placement?.Enabled == true));
        if (_isFullScreen && placement is { Enabled: true })
        {
            cards.Add(new OverlayCardModel(key, definition, placement.Bounds, cards.Count, data));
        }
    }

    // Each overlay add-on supplies its card data here; a kind without data is not offered in the Overlays panel.
    private static object? CreateOverlayCardData(OverlayAddOnKind kind, string accountLabel, XpTrackerRow? trackerRow) =>
        kind switch
        {
            OverlayAddOnKind.Xp => new XpOverlayCardData(accountLabel, trackerRow?.XpPerHourText ?? "— XP/hr"),
            _ => null
        };

    private static string DescribeOverlayCard(object data) =>
        data switch
        {
            XpOverlayCardData xp => xp.XpPerHourText,
            _ => string.Empty
        };
```

- [ ] **Step 5: Replace the overlay event handlers**

Delete `XpOverlayLayer_AccountDropped`, `XpOverlayLayer_BoundsCommitted`, and `SaveXpOverlayBoundsAsync` (including the temporary Task 2 code inside it). Add:

```csharp
    private async void FullscreenOverlayTray_CardToggleRequested(object? sender, OverlayCardToggleRequestedEventArgs args)
    {
        if (!_isFullScreen || !_overlayEditing || !OverlayCardPolicy.IsValidKey(args.Key))
        {
            return;
        }

        try
        {
            await UpdateSettingsAsync(settings => OverlayCardPolicy.WithEnabled(settings, args.Key, args.Enabled));
        }
        catch
        {
            MessageBox.Show(this,
                "The overlay card setting could not be saved. Its previous setting was restored.",
                "FourFold Account Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // Rebuild from saved settings so a failed save also reverts the switch.
            RefreshTrackerRows();
        }
    }

    private async void OverlayLayer_BoundsCommitted(object? sender, OverlayCardBoundsCommittedEventArgs args)
    {
        if (sender is not OverlayCardLayer layer || !_isFullScreen || !_overlayEditing ||
            !IsOverlayCardOwnedByLayer(layer, args.Key))
        {
            return;
        }

        try
        {
            await UpdateSettingsAsync(settings => OverlayCardPolicy.WithBounds(settings, args.Key, args.Bounds));
            RefreshTrackerRows();
        }
        catch
        {
            layer.RestoreSavedBounds();
            MessageBox.Show(this,
                "The overlay placement could not be saved. Its previous position was restored.",
                "FourFold Account Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool IsOverlayCardOwnedByLayer(OverlayCardLayer layer, OverlayCardKey key)
    {
        if (ReferenceEquals(layer, GlobalOverlayLayer))
        {
            return key.AccountId is null;
        }

        var slot = _slotCards.FirstOrDefault(candidate => ReferenceEquals(candidate.OverlayLayer, layer));
        return slot is not null && key.AccountId is { } accountId &&
            _panelSettings.SlotAccountIds[slot.SlotIndex] == accountId &&
            _openAccountIds.Contains(accountId) && slot.View is not null;
    }
```

- [ ] **Step 6: Delete the XP-only overlay files and fix any leftovers**

```bash
git rm src/FourFoldAccountManager.Desktop/Views/XpOverlayLayer.cs \
  src/FourFoldAccountManager.Desktop/Views/XpOverlayCard.xaml \
  src/FourFoldAccountManager.Desktop/Views/XpOverlayCard.xaml.cs \
  src/FourFoldAccountManager.Desktop/Views/XpOverlayAccountChoice.cs \
  src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTray.xaml \
  src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTray.xaml.cs
grep -rn "XpOverlayLayer\|XpOverlayCard\b\|XpOverlayAccountChoice\|FullscreenXpOverlayTray\b\|AccountDragDataFormat\|SetChoices" src --include=*.cs --include=*.xaml | grep -v "/obj/"
```

Expected: the `grep` prints nothing. `FullscreenXpOverlayTabVisibilityState` is intentionally kept, and the pattern does not match it.

- [ ] **Step 7: Update the Settings label and README**

In `src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml`, change `Text="Reveal XP overlay tab"` to `Text="Reveal overlays tab"`.

In `README.md`, replace the sentence at line 34:

```
Tracker behavior and overlays remain available inside the Plugins sidebar.
```

with:

```
Tracker behavior remains available inside the Plugins sidebar. In full screen, open the edge tab (or press the reveal shortcut) to show the Overlays panel, switch each account's XP/hr card on or off, and drag cards into place.
```

- [ ] **Step 8: Build and run every suite**

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: PASS.

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`
Expected: PASS.

Run: `dotnet test src/FourFoldAccountManager.Leaderboard.Tests -c Release --filter "FullyQualifiedName!~LeaderboardStoreTests&FullyQualifiedName!~Readiness"`
Expected: PASS. This is a regression check only; nothing here touches the leaderboard.

- [ ] **Step 9: Manual check in the running app**

Publish a test EXE:

```bash
dotnet publish src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "$TEMP/fourfold-overlay-test"
```

Then verify each item. Record any failure as a bug in this task; do not skip it.
1. Starting from a settings file that already has a placed XP card, enter full screen. The XP card appears in the same place.
2. Open the Overlays panel (edge tab or Ctrl+Alt+Shift+O). Each open account is listed with an **XP/hr** switch that shows its current value. No Global section is shown.
3. Switch XP on for an account that never had it. The card appears at the top-left of that client and can be dragged and resized.
4. Switch it off and on again. It returns to the position it was dragged to.
5. Click **Done**. Clicks and keys reach the game through the empty areas, and over the cards too, because the cards ignore input outside edit mode.
6. Restart the app and enter full screen. Every card's on/off state and position is unchanged.
7. Leave full screen with Esc during a drag. There is no error, and the card's saved position is unchanged the next time.

- [ ] **Step 10: Commit**

```bash
git add -A src/FourFoldAccountManager.Desktop README.md
git commit -m "feat: host full-screen XP card as a switchable overlay add-on"
```
