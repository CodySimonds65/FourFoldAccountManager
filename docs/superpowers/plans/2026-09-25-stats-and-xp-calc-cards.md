# Stats and XP Calc Overlay Cards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-account **Stats** and **XP calc** full-screen overlay cards. They are fed by the XP tracker's latest snapshots and by per-account XP calculator targets saved in settings. This plan also puts the sidebar Timer tab inside the same card container as the other plugin tabs.

**Architecture:**
- **Core** stores target levels in `PanelSettings.XpCalculatorTargetLevels` (edited through `XpCalculatorTargets`, loaded tolerantly), and builds card text in pure `StatsCardContent` and `XpCalcCardContent` helpers.
- **Desktop:**
  - `XpTrackerCoordinator` exposes each open account's latest snapshot.
  - The sidebar XP calc fills in and saves targets.
  - `StatsCardData` and `XpCalcCardData` carry the content into `Viewbox`-wrapped templates.
  - `MainWindow.CreateOverlayCardData` supplies the data for the new catalog kinds.

**Tech Stack:** .NET 10, WPF (net10.0-windows), System.Text.Json, xUnit 2.9.

**Spec:** `docs/superpowers/specs/2026-09-25-stats-and-xp-calc-cards-design.md`. It builds on `2026-09-25-overlay-add-ons-design.md` and `2026-09-25-timer-plugin-design.md`.

## Global Constraints

- Work on branch `feature/timer-plugin` (spec commit `157fc8f`). Do not switch branches.
- **Card data source:** the cards use only `XpTrackerCoordinator.GetLatestSnapshot(accountId)`, which is the tracker's newest successful fetch this session, or `null`. They make no new website requests.
- **Target storage:** `PanelSettings.XpCalculatorTargetLevels` is an `IReadOnlyDictionary<Guid, long>`.
  - Loading and validation drop empty account IDs and non-positive levels. A wrong-shaped value loads as an empty map instead of failing the load.
  - Every place that rebuilds `PanelSettings` field by field copies the map: `SettingsStore.Validate`, and `PanelLayoutPolicy` `WithLayout`, `Assign`, `ClearAccount` and `CopySettings`. `ClearAccount` removes that account's entry.
- **Sidebar XP calc:**
  - When the account changes, the target box shows that account's saved target, or blank.
  - A valid positive target saves after a 500 ms pause with no further typing. Clearing the box removes the saved target.
  - Invalid text never changes the saved target. Programmatic fills are never saved.
- **Status texts, verbatim:** "Waiting for profile…" (no snapshot yet); "Target reached" (saved target at or below the current level); "· stale" at the end of a card header when the tracker is stale.
- **XP calc target:** the saved target if it is above the current level, otherwise the next level. Lines read "Class Level → Target" and "N,NNN XP · L levels to go", with "level" singular when L is 1.
- **Stats card:** header "Class Level", counts "▲A ▼B", summary "▲A ▼B · mean ±x.x%", and 9 rows (stat label, profile value, average, delta %). Deltas above average are gold; the rest are muted. Numbers use invariant culture.
- **Catalog order:** XP/hr, Stats, XP calc, Timer.
  - `OverlayAddOnKind.Stats = 2`: Account scope, "Stats", default 260×220, minimum 180×150.
  - `OverlayAddOnKind.XpCalc = 3`: Account scope, "XP calc", default 220×56, minimum 150×40.
  - Existing kind numbers are unchanged.
- **Card templates** wrap their content in `Viewbox Stretch="Uniform" StretchDirection="DownOnly"`, so nothing is clipped at the minimum size.
- **Timer tab:** wrapped in `SidebarCardStyle` with `Padding="14"`, under the heading "TIMER" (size 10, bold, gold) and "Speedrun stopwatch" (size 16, semibold).
- **WPF tests** run through `WpfTestHost.Run(...)`. Never create another `Application`.
- **Commands** (run from the repo root):
  - `dotnet build FourFoldAccountManager.sln -c Release` must show 0 warnings and 0 errors.
  - `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
  - `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`

## Review Focus

- **Switching accounts right after typing a target.** The typed target must be saved for the account it was typed on, not the newly selected one. Pinned in Task 3 by `SwitchingAccountsSavesThePendingTargetForThePreviousAccount`.
- **Leveling past a saved target.** When the player levels past a saved target, the card shows "Target reached" rather than a negative or invalid projection. Pinned in Task 2 by `SavedTargetAtOrBelowTheCurrentLevelIsReached`.
- **Hand-edited or older settings.** A string, list, negative level or non-GUID key must still load. Pinned in Task 1 by `BadTargetDataStillLoads`.
- **Failed fetches.** A card whose tracker fetch has failed keeps showing the last data with "· stale" in its header. Pinned in Task 4 by `StaleCardsMarkTheirHeader`.
- **Minimum size.** Both new cards shrink to fit at their minimum size and never clip. Pinned in Task 4 by `CardsShrinkToFitAtTheirMinimumSize`.

---

## File Structure

**Core (`src/FourFoldAccountManager.Core`)**
- `Models/PanelSettings.cs`: adds `XpCalculatorTargetLevels`.
- `Models/LenientTargetLevelsJsonConverter.cs` (new): tolerant JSON for the targets map.
- `Calculation/XpCalculatorTargets.cs` (new): get, set and remove targets, plus normalize and remove-account helpers.
- `Data/SettingsStore.cs`, `Panel/PanelLayoutPolicy.cs`: copy, normalize and remove targets.
- `Calculation/CharacterStatLabels.cs` (new): short stat labels, moved out of `ClassComparisonPanel`.
- `Overlay/StatsCardContent.cs`, `Overlay/XpCalcCardContent.cs` (new): card text.
- `Models/OverlayAddOnKind.cs`, `Overlay/OverlayAddOnCatalog.cs`: the Stats and XpCalc kinds.

**Desktop (`src/FourFoldAccountManager.Desktop`)**
- `Services/XpTrackerCoordinator.cs`: `GetLatestSnapshot`; `PollOnceAsync` becomes internal.
- `Views/ExperienceCalculatorPanel.xaml.cs`: saved-target fill, debounced save, and the `TargetLevelChanged` event.
- `Views/PluginSidebar.xaml.cs`: forwards the target event and the saved target.
- `Views/ClassComparisonPanel.xaml.cs`: uses `CharacterStatLabels`.
- `Views/OverlayCardText.cs`, `Views/StatsCardData.cs`, `Views/XpCalcCardData.cs` (new).
- `Resources/OverlayCardTemplates.xaml`: Stats and XP calc templates.
- `Views/TimerPanel.xaml(.cs)`: card container and heading.
- `MainWindow.xaml.cs`: target save handler, saved target passed to the sidebar, card data, and stale flag.
- `README.md`: the new cards and the collapsible sidebar.

**Tests**
- **Core:** `Calculation/XpCalculatorTargetsTests.cs`, `Data/SettingsStoreXpTargetTests.cs`, `Panel/PanelLayoutPolicyTests.cs` (extended), `Overlay/StatsCardContentTests.cs`, `Overlay/XpCalcCardContentTests.cs`, and `Overlay/OverlayCardPolicyTests.cs` (catalog test updated).
- **Desktop:** `VisualTree.cs`, `Services/XpTrackerSnapshotTests.cs`, `Views/ExperienceCalculatorTargetTests.cs`, `Views/AccountOverlayCardTests.cs`, and `Views/TimerPanelTests.cs` (extended).

---

### Task 1: Saved XP calculator targets in settings (Core)

**Files:**
- Create: `src/FourFoldAccountManager.Core/Models/LenientTargetLevelsJsonConverter.cs`
- Create: `src/FourFoldAccountManager.Core/Calculation/XpCalculatorTargets.cs`
- Modify: `src/FourFoldAccountManager.Core/Models/PanelSettings.cs`
- Modify: `src/FourFoldAccountManager.Core/Data/SettingsStore.cs` (`Validate`)
- Modify: `src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs` (4 copy sites)
- Test: `src/FourFoldAccountManager.Core.Tests/Calculation/XpCalculatorTargetsTests.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Data/SettingsStoreXpTargetTests.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Panel/PanelLayoutPolicyTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `PanelSettings.XpCalculatorTargetLevels` (`IReadOnlyDictionary<Guid, long>`, `{ get; init; }`, default empty).
  - `static class XpCalculatorTargets` (namespace `FourFoldAccountManager.Core.Calculation`) with:
    - `long? Get(PanelSettings, Guid accountId)`
    - `PanelSettings WithTarget(PanelSettings, Guid accountId, long? targetLevel)`: `null` removes the target; a level of 0 or below throws `ArgumentOutOfRangeException`; an empty account ID throws `ArgumentException`.
    - `IReadOnlyDictionary<Guid, long> Normalize(IReadOnlyDictionary<Guid, long>?)`
    - `IReadOnlyDictionary<Guid, long> RemoveAccount(IReadOnlyDictionary<Guid, long>, Guid)`

- [ ] **Step 1: Write the failing tests**

`src/FourFoldAccountManager.Core.Tests/Calculation/XpCalculatorTargetsTests.cs`:

```csharp
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Calculation;

public sealed class XpCalculatorTargetsTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    [Fact]
    public void WithTargetAddsReplacesAndRemovesOnlyThatAccount()
    {
        var settings = XpCalculatorTargets.WithTarget(PanelSettings.Default, Alice, 50);
        settings = XpCalculatorTargets.WithTarget(settings, Bob, 20);
        settings = XpCalculatorTargets.WithTarget(settings, Alice, 60);

        Assert.Equal(60, XpCalculatorTargets.Get(settings, Alice));
        Assert.Equal(20, XpCalculatorTargets.Get(settings, Bob));

        settings = XpCalculatorTargets.WithTarget(settings, Alice, null);

        Assert.Null(XpCalculatorTargets.Get(settings, Alice));
        Assert.Equal(20, XpCalculatorTargets.Get(settings, Bob));
    }

    [Fact]
    public void WithTargetRejectsNonPositiveLevelsAndEmptyAccounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => XpCalculatorTargets.WithTarget(PanelSettings.Default, Alice, 0));
        Assert.Throws<ArgumentException>(() => XpCalculatorTargets.WithTarget(PanelSettings.Default, Guid.Empty, 5));
    }

    [Fact]
    public void NormalizeDropsEmptyAccountsAndNonPositiveLevels()
    {
        var normalized = XpCalculatorTargets.Normalize(new Dictionary<Guid, long>
        {
            [Alice] = 50,
            [Bob] = 0,
            [Guid.Empty] = 7
        });

        Assert.Equal(new Dictionary<Guid, long> { [Alice] = 50 }, normalized);
        Assert.Empty(XpCalculatorTargets.Normalize(null));
    }

    [Fact]
    public void RemoveAccountDropsOnlyThatAccount()
    {
        var remaining = XpCalculatorTargets.RemoveAccount(
            new Dictionary<Guid, long> { [Alice] = 50, [Bob] = 20 }, Alice);

        Assert.Equal(new Dictionary<Guid, long> { [Bob] = 20 }, remaining);
    }
}
```

`src/FourFoldAccountManager.Core.Tests/Data/SettingsStoreXpTargetTests.cs`:

```csharp
using System.Text.Json.Nodes;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Data;

public sealed class SettingsStoreXpTargetTests : IDisposable
{
    private static readonly Guid Alice = Guid.NewGuid();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-xp-targets-{Guid.NewGuid():N}");
    private readonly SettingsStore _store;

    public SettingsStoreXpTargetTests()
    {
        Directory.CreateDirectory(_root);
        _store = new SettingsStore(new LocalDataPaths(_root));
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task TargetsSurviveARestart()
    {
        await _store.SaveAsync(PanelSettings.Default with
        {
            XpCalculatorTargetLevels = new Dictionary<Guid, long> { [Alice] = 50 }
        });

        Assert.Equal(new Dictionary<Guid, long> { [Alice] = 50 }, (await _store.LoadAsync()).XpCalculatorTargetLevels);
    }

    [Fact]
    public async Task BadTargetDataStillLoads()
    {
        var bob = Guid.NewGuid();
        await WriteSettingsAsync(json => json["xpCalculatorTargetLevels"] = new JsonObject
        {
            [Alice.ToString()] = 50,
            ["not-a-guid"] = 5,
            [bob.ToString()] = -3,
            [Guid.NewGuid().ToString()] = "x",
            [Guid.Empty.ToString()] = 7
        });

        Assert.Equal(new Dictionary<Guid, long> { [Alice] = 50 }, (await _store.LoadAsync()).XpCalculatorTargetLevels);

        foreach (JsonNode? wrongShape in new JsonNode?[] { JsonValue.Create("oops"), new JsonArray(1, 2), null })
        {
            await WriteSettingsAsync(json => json["xpCalculatorTargetLevels"] = wrongShape?.DeepClone());
            Assert.Empty((await _store.LoadAsync()).XpCalculatorTargetLevels);
        }
    }

    [Fact]
    public async Task SettingsWrittenBeforeTargetsExistedLoadWithNone()
    {
        await WriteSettingsAsync(json => json.Remove("xpCalculatorTargetLevels"));

        Assert.Empty((await _store.LoadAsync()).XpCalculatorTargetLevels);
    }

    private async Task WriteSettingsAsync(Action<JsonObject> edit)
    {
        await _store.SaveAsync(PanelSettings.Default);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!.AsObject();
        edit(json);
        await File.WriteAllTextAsync(SettingsPath, json.ToJsonString());
    }
}
```

Append this test inside the class in `src/FourFoldAccountManager.Core.Tests/Panel/PanelLayoutPolicyTests.cs`:

```csharp
    [Fact]
    public void SettingsTransformsPreserveXpTargetsAndClearAccountRemovesTheAccountsTarget()
    {
        var accountId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var targets = new Dictionary<Guid, long> { [accountId] = 50, [otherId] = 20 };
        var settings = PanelSettings.Default with
        {
            SlotAccountIds = [accountId, otherId, null, null, null],
            XpCalculatorTargetLevels = targets
        };

        Assert.Equal(targets, PanelLayoutPolicy.WithLayout(settings, PanelLayout.OneByTwo).XpCalculatorTargetLevels);
        Assert.Equal(targets, PanelLayoutPolicy.Assign(settings, 2, Guid.NewGuid()).XpCalculatorTargetLevels);
        Assert.Equal(targets, PanelLayoutPolicy.WithSplitState(settings,
            new PanelSplitState("2x2.rows", [0.7, 0.3])).XpCalculatorTargetLevels);
        Assert.Equal(targets, PanelLayoutPolicy.ResetSplitStates(settings).XpCalculatorTargetLevels);
        Assert.Equal(new Dictionary<Guid, long> { [otherId] = 20 },
            PanelLayoutPolicy.ClearAccount(settings, accountId).XpCalculatorTargetLevels);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: the build FAILS with `'PanelSettings' does not contain a definition for 'XpCalculatorTargetLevels'` and `The name 'XpCalculatorTargets' does not exist`.

- [ ] **Step 3: Add the tolerant converter and the setting**

`src/FourFoldAccountManager.Core/Models/LenientTargetLevelsJsonConverter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

// XP calculator targets arrived after settings files existed, so a wrong-shaped value loads as "no targets"
// and unreadable entries are skipped instead of failing the whole settings load.
public sealed class LenientTargetLevelsJsonConverter : JsonConverter<IReadOnlyDictionary<Guid, long>>
{
    public override IReadOnlyDictionary<Guid, long> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var targets = new Dictionary<Guid, long>();
        JsonElement element;
        try
        {
            element = JsonElement.ParseValue(ref reader);
        }
        catch (JsonException)
        {
            return targets;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return targets;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (Guid.TryParse(property.Name, out var accountId) &&
                property.Value.ValueKind == JsonValueKind.Number &&
                property.Value.TryGetInt64(out var level))
            {
                targets.TryAdd(accountId, level);
            }
        }

        return targets;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<Guid, long> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (accountId, level) in value)
        {
            writer.WriteNumber(accountId.ToString(), level);
        }

        writer.WriteEndObject();
    }
}
```

In `src/FourFoldAccountManager.Core/Models/PanelSettings.cs`, add this after the `GameViewportSizes` property:

```csharp
    [JsonConverter(typeof(LenientTargetLevelsJsonConverter))]
    public IReadOnlyDictionary<Guid, long> XpCalculatorTargetLevels { get; init; } = new Dictionary<Guid, long>();
```

- [ ] **Step 4: Add the targets helper**

`src/FourFoldAccountManager.Core/Calculation/XpCalculatorTargets.cs`:

```csharp
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Calculation;

// Per-account XP calculator target levels, typed in the sidebar and read by the XP calc overlay card.
public static class XpCalculatorTargets
{
    public static long? Get(PanelSettings settings, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.XpCalculatorTargetLevels.TryGetValue(accountId, out var level) ? level : null;
    }

    public static PanelSettings WithTarget(PanelSettings settings, Guid accountId, long? targetLevel)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(accountId));
        }

        if (targetLevel is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetLevel), targetLevel, "Target levels must be positive.");
        }

        var targets = new Dictionary<Guid, long>(settings.XpCalculatorTargetLevels);
        if (targetLevel is { } level)
        {
            targets[accountId] = level;
        }
        else
        {
            targets.Remove(accountId);
        }

        return settings with { XpCalculatorTargetLevels = targets };
    }

    public static IReadOnlyDictionary<Guid, long> Normalize(IReadOnlyDictionary<Guid, long>? targets) =>
        (targets ?? new Dictionary<Guid, long>())
            .Where(entry => entry.Key != Guid.Empty && entry.Value > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

    public static IReadOnlyDictionary<Guid, long> RemoveAccount(IReadOnlyDictionary<Guid, long> targets, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return targets.Where(entry => entry.Key != accountId).ToDictionary(entry => entry.Key, entry => entry.Value);
    }
}
```

- [ ] **Step 5: Normalize and copy the targets**

In `src/FourFoldAccountManager.Core/Data/SettingsStore.cs`:

1. Add `using FourFoldAccountManager.Core.Calculation;`.
2. In `Validate`'s returned object initializer, directly after `PluginsSidebarExpanded = settings.PluginsSidebarExpanded,`, add:

```csharp
            XpCalculatorTargetLevels = XpCalculatorTargets.Normalize(settings.XpCalculatorTargetLevels),
```

In `src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs`:

1. Add `using FourFoldAccountManager.Core.Calculation;` if it is not already present.
2. Directly after each of the 4 lines `PluginsSidebarExpanded = settings.PluginsSidebarExpanded,` (in `WithLayout`, `Assign`, `ClearAccount` and `CopySettings`), add:

```csharp
            XpCalculatorTargetLevels = settings.XpCalculatorTargetLevels,
```

3. In `ClearAccount` only, change the line you just added to:

```csharp
            XpCalculatorTargetLevels = XpCalculatorTargets.RemoveAccount(settings.XpCalculatorTargetLevels, accountId),
```

To verify, run: `grep -c "XpCalculatorTargetLevels = " src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs`
Expected: `4`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: PASS, including 4 `XpCalculatorTargetsTests`, 3 `SettingsStoreXpTargetTests`, and the new `PanelLayoutPolicyTests` test.

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/FourFoldAccountManager.Core src/FourFoldAccountManager.Core.Tests
git commit -m "feat: save XP calculator target levels per account"
```

---

### Task 2: Card content, stat labels, and catalog entries (Core)

**Files:**
- Create: `src/FourFoldAccountManager.Core/Calculation/CharacterStatLabels.cs`
- Create: `src/FourFoldAccountManager.Core/Overlay/StatsCardContent.cs`
- Create: `src/FourFoldAccountManager.Core/Overlay/XpCalcCardContent.cs`
- Modify: `src/FourFoldAccountManager.Core/Models/OverlayAddOnKind.cs`
- Modify: `src/FourFoldAccountManager.Core/Overlay/OverlayAddOnCatalog.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Views/ClassComparisonPanel.xaml.cs`: remove the private `Labels` dictionary and use `CharacterStatLabels.Short`
- Test: `src/FourFoldAccountManager.Core.Tests/Overlay/StatsCardContentTests.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Overlay/XpCalcCardContentTests.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Overlay/OverlayCardPolicyTests.cs` (catalog test)

**Interfaces:**
- Consumes these existing types:
  - `ClassComparisonDisplayState` (positional: `IsAvailable, Status, ActiveClassName, Level, SourceUpdated, Rows, AboveCount, BelowCount, EqualCount, MeanPercentageDifference`) and `ClassComparisonDisplayState.FromSnapshot`.
  - `ClassStatComparison(Stat, ProfileValue, Average, Difference, PercentageDifference, Direction)`, `ComparisonDirection`, and `CharacterStat`.
  - `ExperienceCalculatorState.FromSnapshot`, `.WithTarget(long)`, `.Profile`, `.ClassName`, `.CurrentLevel`, `.IsValid`, `.Status`, `.RemainingXpText` (`N0`, invariant) and `.LevelsRemaining`.
  - `PlayerProgressSnapshot` and `ClassProfileSnapshot`.
- Produces:
  - `static class CharacterStatLabels` with `string Short(CharacterStat)`, returning HP, SP, ATT, MAG, SKL, SPD, LCK, DEF or RES.
  - In namespace `FourFoldAccountManager.Core.Overlay`:
    - `record StatsCardRow(string StatLabel, string ProfileText, string AverageText, string DeltaText, ComparisonDirection Direction)`
    - `record StatsCardContent(bool IsAvailable, string Header, string CountsText, string SummaryText, string Status, IReadOnlyList<StatsCardRow> Rows)`, with `const string WaitingStatus`, `static FromSnapshot(PlayerProgressSnapshot?)` and `static FromState(ClassComparisonDisplayState)`.
    - `record XpCalcCardContent(bool IsAvailable, string TargetLine, string RemainingLine, string Status, long? TargetLevel)`, with `const string WaitingStatus`, `const string TargetReached` and `static FromSnapshot(PlayerProgressSnapshot?, long? savedTargetLevel)`.
  - `OverlayAddOnKind.Stats = 2` and `OverlayAddOnKind.XpCalc = 3`. The catalog order becomes Xp, Stats, XpCalc, Timer.

- [ ] **Step 1: Write the failing tests**

`src/FourFoldAccountManager.Core.Tests/Overlay/StatsCardContentTests.cs`:

```csharp
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Tracking;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Overlay;

public sealed class StatsCardContentTests
{
    [Fact]
    public void NoSnapshotWaitsForTheProfile()
    {
        var content = StatsCardContent.FromSnapshot(null);

        Assert.False(content.IsAvailable);
        Assert.Equal("Waiting for profile…", content.Status);
        Assert.Empty(content.Rows);
        Assert.Equal(string.Empty, content.CountsText);
    }

    [Fact]
    public void AvailableComparisonBuildsHeaderSummaryAndRows()
    {
        var state = new ClassComparisonDisplayState(true, "Profile stats loaded.", "Mage", 42, null,
        [
            new ClassStatComparison(CharacterStat.Hp, 1_100, 1_000, 100, 10.0, ComparisonDirection.Above),
            new ClassStatComparison(CharacterStat.Defense, 90, 100, -10, -10.0, ComparisonDirection.Below),
            new ClassStatComparison(CharacterStat.Luck, 50, 50, 0, 0.0, ComparisonDirection.Equal)
        ], 1, 1, 1, 0.0);

        var content = StatsCardContent.FromState(state);

        Assert.True(content.IsAvailable);
        Assert.Equal("Mage 42", content.Header);
        Assert.Equal("▲1 ▼1", content.CountsText);
        Assert.Equal("▲1 ▼1 · mean 0.0%", content.SummaryText);
        Assert.Equal(
        [
            new StatsCardRow("HP", "1,100", "1,000", "+10.0%", ComparisonDirection.Above),
            new StatsCardRow("DEF", "90", "100", "-10.0%", ComparisonDirection.Below),
            new StatsCardRow("LCK", "50", "50", "0.0%", ComparisonDirection.Equal)
        ], content.Rows);
    }

    [Fact]
    public void UnavailableComparisonShowsItsStatus()
    {
        var noClass = new PlayerProgressSnapshot("Alice", null, new Dictionary<string, ClassProfileSnapshot>(), []);
        var unknownClass = new PlayerProgressSnapshot("Alice", "Nobody", new Dictionary<string, ClassProfileSnapshot>
        {
            ["Nobody"] = new(3, 0, 10, null) { ClassName = "Nobody" }
        }, []);

        var missing = StatsCardContent.FromSnapshot(noClass);
        var unknown = StatsCardContent.FromSnapshot(unknownClass);

        Assert.False(missing.IsAvailable);
        Assert.Equal("The active class is unavailable.", missing.Status);
        Assert.Equal(string.Empty, missing.Header);
        Assert.False(unknown.IsAvailable);
        Assert.Equal("Nobody 3", unknown.Header);
        Assert.Equal("No class average is available for Nobody.", unknown.Status);
    }

    [Theory]
    [InlineData(CharacterStat.Hp, "HP")]
    [InlineData(CharacterStat.Sp, "SP")]
    [InlineData(CharacterStat.Attack, "ATT")]
    [InlineData(CharacterStat.Magic, "MAG")]
    [InlineData(CharacterStat.Skill, "SKL")]
    [InlineData(CharacterStat.Speed, "SPD")]
    [InlineData(CharacterStat.Luck, "LCK")]
    [InlineData(CharacterStat.Defense, "DEF")]
    [InlineData(CharacterStat.Resistance, "RES")]
    public void StatLabelsMatchTheStatsTab(CharacterStat stat, string expected) =>
        Assert.Equal(expected, CharacterStatLabels.Short(stat));
}
```

`src/FourFoldAccountManager.Core.Tests/Overlay/XpCalcCardContentTests.cs`:

```csharp
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Tracking;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Overlay;

public sealed class XpCalcCardContentTests
{
    [Fact]
    public void NoSnapshotWaitsForTheProfile()
    {
        var content = XpCalcCardContent.FromSnapshot(null, savedTargetLevel: 50);

        Assert.False(content.IsAvailable);
        Assert.Equal("Waiting for profile…", content.Status);
        Assert.Null(content.TargetLevel);
    }

    [Fact]
    public void WithoutASavedTargetTheCardAimsAtTheNextLevel()
    {
        var content = XpCalcCardContent.FromSnapshot(Snapshot(), savedTargetLevel: null);

        Assert.True(content.IsAvailable);
        Assert.Equal("Warrior 2 → 3", content.TargetLine);
        Assert.Equal("25 XP · 1 level to go", content.RemainingLine);
        Assert.Equal(3, content.TargetLevel);
    }

    [Fact]
    public void SavedTargetAboveTheCurrentLevelIsUsed()
    {
        var snapshot = Snapshot();
        var expectedXp = ExperienceCalculatorState.FromSnapshot(snapshot).WithTarget(5).RemainingXpText;

        var content = XpCalcCardContent.FromSnapshot(snapshot, savedTargetLevel: 5);

        Assert.Equal("Warrior 2 → 5", content.TargetLine);
        Assert.Equal($"{expectedXp} XP · 3 levels to go", content.RemainingLine);
        Assert.Equal(5, content.TargetLevel);
    }

    [Theory]
    [InlineData(2L)]
    [InlineData(1L)]
    public void SavedTargetAtOrBelowTheCurrentLevelIsReached(long savedTarget)
    {
        var content = XpCalcCardContent.FromSnapshot(Snapshot(), savedTarget);

        Assert.True(content.IsAvailable);
        Assert.Equal($"Warrior 2 → {savedTarget}", content.TargetLine);
        Assert.Equal("Target reached", content.RemainingLine);
    }

    [Fact]
    public void MissingActiveClassShowsTheCalculatorStatus()
    {
        var content = XpCalcCardContent.FromSnapshot(
            new PlayerProgressSnapshot("Alice", null, new Dictionary<string, ClassProfileSnapshot>(), []), 50);

        Assert.False(content.IsAvailable);
        Assert.Equal("The active class is unavailable.", content.Status);
    }

    private static PlayerProgressSnapshot Snapshot() =>
        new("Alice", "Warrior", new Dictionary<string, ClassProfileSnapshot>
        {
            ["Warrior"] = new(2, 5, 30, null) { ClassName = "Warrior" }
        }, []);
}
```

In `src/FourFoldAccountManager.Core.Tests/Overlay/OverlayCardPolicyTests.cs`, replace the test `CatalogRegistersTheXpAndTimerCards` with:

```csharp
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
```

Before relying on this change, run `grep -rn "(OverlayAddOnKind)2\|(OverlayAddOnKind)3\|\"kind\"\] = 2\|\"kind\"\] = 3" src --include=*.cs`. If any existing test uses 2 or 3 as an unknown kind, change it to 99.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: the build FAILS with `The type or namespace name 'StatsCardContent' could not be found` and `'OverlayAddOnKind' does not contain a definition for 'Stats'`.

- [ ] **Step 3: Add the stat labels and use them in the Stats tab**

`src/FourFoldAccountManager.Core/Calculation/CharacterStatLabels.cs`:

```csharp
namespace FourFoldAccountManager.Core.Calculation;

public static class CharacterStatLabels
{
    public static string Short(CharacterStat stat) => stat switch
    {
        CharacterStat.Hp => "HP",
        CharacterStat.Sp => "SP",
        CharacterStat.Attack => "ATT",
        CharacterStat.Magic => "MAG",
        CharacterStat.Skill => "SKL",
        CharacterStat.Speed => "SPD",
        CharacterStat.Luck => "LCK",
        CharacterStat.Defense => "DEF",
        CharacterStat.Resistance => "RES",
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, "Unknown character stat.")
    };
}
```

In `src/FourFoldAccountManager.Desktop/Views/ClassComparisonPanel.xaml.cs`, delete the whole `private static readonly IReadOnlyDictionary<CharacterStat, string> Labels = ...;` field. Then replace `Labels[row.Stat],` with `CharacterStatLabels.Short(row.Stat),`.

- [ ] **Step 4: Add the card content helpers**

`src/FourFoldAccountManager.Core/Overlay/StatsCardContent.cs`:

```csharp
using System.Globalization;
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Overlay;

public sealed record StatsCardRow(
    string StatLabel,
    string ProfileText,
    string AverageText,
    string DeltaText,
    ComparisonDirection Direction);

// Text for the full-screen Stats card: the active class's comparison against its class average.
public sealed record StatsCardContent(
    bool IsAvailable,
    string Header,
    string CountsText,
    string SummaryText,
    string Status,
    IReadOnlyList<StatsCardRow> Rows)
{
    public const string WaitingStatus = "Waiting for profile…";

    public static StatsCardContent FromSnapshot(PlayerProgressSnapshot? snapshot) =>
        snapshot is null
            ? new StatsCardContent(false, string.Empty, string.Empty, string.Empty, WaitingStatus, [])
            : FromState(ClassComparisonDisplayState.FromSnapshot(snapshot));

    public static StatsCardContent FromState(ClassComparisonDisplayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var header = state.ActiveClassName is null
            ? string.Empty
            : state.Level is { } level
                ? string.Create(CultureInfo.InvariantCulture, $"{state.ActiveClassName} {level}")
                : state.ActiveClassName;
        if (!state.IsAvailable)
        {
            return new StatsCardContent(false, header, string.Empty, string.Empty, state.Status, []);
        }

        var counts = string.Create(CultureInfo.InvariantCulture, $"▲{state.AboveCount} ▼{state.BelowCount}");
        var summary = state.MeanPercentageDifference is { } mean ? $"{counts} · mean {Percent(mean)}" : counts;
        var rows = state.Rows.Select(row => new StatsCardRow(
                CharacterStatLabels.Short(row.Stat),
                row.ProfileValue.ToString("N0", CultureInfo.InvariantCulture),
                row.Average.ToString("N0", CultureInfo.InvariantCulture),
                row.PercentageDifference is { } percentage ? Percent(percentage) : "—",
                row.Direction))
            .ToArray();
        return new StatsCardContent(true, header, counts, summary, string.Empty, rows);
    }

    private static string Percent(double value) =>
        value.ToString("+#,##0.0;-#,##0.0;0.0", CultureInfo.InvariantCulture) + "%";
}
```

`src/FourFoldAccountManager.Core/Overlay/XpCalcCardContent.cs`:

```csharp
using System.Globalization;
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Overlay;

// Text for the full-screen XP calc card: XP and levels left to the saved target, or to the next level.
public sealed record XpCalcCardContent(
    bool IsAvailable,
    string TargetLine,
    string RemainingLine,
    string Status,
    long? TargetLevel)
{
    public const string WaitingStatus = "Waiting for profile…";

    public const string TargetReached = "Target reached";

    public static XpCalcCardContent FromSnapshot(PlayerProgressSnapshot? snapshot, long? savedTargetLevel)
    {
        if (snapshot is null)
        {
            return new XpCalcCardContent(false, string.Empty, string.Empty, WaitingStatus, null);
        }

        var state = ExperienceCalculatorState.FromSnapshot(snapshot);
        if (state.Profile is null)
        {
            return new XpCalcCardContent(false, string.Empty, string.Empty, state.Status, null);
        }

        var level = state.CurrentLevel;
        var classLine = string.Create(CultureInfo.InvariantCulture, $"{state.ClassName} {level}");
        if (savedTargetLevel is { } saved && saved > 0 && saved <= level)
        {
            return new XpCalcCardContent(true,
                string.Create(CultureInfo.InvariantCulture, $"{classLine} → {saved}"), TargetReached, string.Empty, saved);
        }

        var target = savedTargetLevel is { } wanted && wanted > level ? wanted : level + 1;
        var targetLine = string.Create(CultureInfo.InvariantCulture, $"{classLine} → {target}");
        var projected = state.WithTarget(target);
        if (!projected.IsValid)
        {
            return new XpCalcCardContent(false, targetLine, string.Empty, projected.Status, target);
        }

        var levels = projected.LevelsRemaining;
        var remaining = string.Create(CultureInfo.InvariantCulture,
            $"{projected.RemainingXpText} XP · {levels} {(levels == 1 ? "level" : "levels")} to go");
        return new XpCalcCardContent(true, targetLine, remaining, string.Empty, target);
    }
}
```

- [ ] **Step 5: Register the Stats and XP calc cards**

In `src/FourFoldAccountManager.Core/Models/OverlayAddOnKind.cs`, change the enum body to:

```csharp
    Xp = 0,
    Timer = 1,
    Stats = 2,
    XpCalc = 3
```

In `src/FourFoldAccountManager.Core/Overlay/OverlayAddOnCatalog.cs`, replace the entries in the `All` array with:

```csharp
        new OverlayAddOnDefinition(OverlayAddOnKind.Xp, OverlayAddOnScope.Account, "XP/hr", 200, 52, 144, 40),
        new OverlayAddOnDefinition(OverlayAddOnKind.Stats, OverlayAddOnScope.Account, "Stats", 260, 220, 180, 150),
        new OverlayAddOnDefinition(OverlayAddOnKind.XpCalc, OverlayAddOnScope.Account, "XP calc", 220, 56, 150, 40),
        new OverlayAddOnDefinition(OverlayAddOnKind.Timer, OverlayAddOnScope.Global, "Timer", 220, 60, 150, 44)
```

These kinds have no card data in `MainWindow` until Task 4. Until then they are not offered in the Overlays panel, because `CreateOverlayCardData` returns `null` for them.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: PASS, including `StatsCardContentTests`, `XpCalcCardContentTests` and `CatalogRegistersEveryAddOnInPanelOrder`.

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`
Expected: PASS. This checks that the catalog reorder and the label move broke nothing.

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/FourFoldAccountManager.Core src/FourFoldAccountManager.Core.Tests src/FourFoldAccountManager.Desktop/Views/ClassComparisonPanel.xaml.cs
git commit -m "feat: add stats and XP calc card content and catalog entries"
```

---

### Task 3: Tracker snapshots and saved targets in the sidebar XP calc (Desktop)

**Files:**
- Modify: `src/FourFoldAccountManager.Desktop/Services/XpTrackerCoordinator.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Views/ExperienceCalculatorPanel.xaml.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Views/PluginSidebar.xaml.cs`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`
- Test: `src/FourFoldAccountManager.Desktop.Tests/Services/XpTrackerSnapshotTests.cs`
- Test: `src/FourFoldAccountManager.Desktop.Tests/Views/ExperienceCalculatorTargetTests.cs`

**Interfaces:**
- Consumes (from Task 1): `XpCalculatorTargets.Get` and `XpCalculatorTargets.WithTarget`. Existing types: `IPlayerProfileTransport`, `PlayerProfileService`, `RankingEntry`, and the internal constructor `XpTrackerCoordinator(LocalDataPaths, Func<CancellationToken, Task>?, PlayerProfileService?)`.
- Produces:
  - `XpTrackerCoordinator`:
    - `PlayerProgressSnapshot? GetLatestSnapshot(Guid accountId)`
    - `internal Task PollOnceAsync(CancellationToken)`
  - `ExperienceCalculatorPanel`:
    - `SetSnapshot(AccountProfile, PlayerProgressSnapshot, long? savedTargetLevel = null)`
    - `event Action<Guid, long?>? TargetLevelChanged`
    - `internal void FlushPendingTargetSave()`
    - `internal TimeSpan TargetSaveDelay`
  - `PluginSidebar`:
    - `event Action<Guid, long?>? XpTargetLevelChanged`
    - `SetProfileSnapshot(PlayerProgressSnapshot?, long? savedXpTargetLevel = null)`

- [ ] **Step 1: Write the failing tests**

`src/FourFoldAccountManager.Desktop.Tests/Services/XpTrackerSnapshotTests.cs`:

```csharp
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Tracking;
using FourFoldAccountManager.Desktop.Services;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Services;

public sealed class XpTrackerSnapshotTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-tracker-{Guid.NewGuid():N}");

    public XpTrackerSnapshotTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task LatestSnapshotAppearsOnlyAfterASuccessfulFetch()
    {
        var snapshot = new PlayerProgressSnapshot("Player", "Warrior", new Dictionary<string, ClassProfileSnapshot>
        {
            ["Warrior"] = new(2, 5, 30, null) { ClassName = "Warrior" }
        }, []);
        await using var tracker = new XpTrackerCoordinator(new LocalDataPaths(_root), _ => Task.CompletedTask,
            new PlayerProfileService(new FixedTransport(snapshot)));
        var accountId = Guid.NewGuid();
        tracker.Start(accountId, "Player", 42);

        Assert.Null(tracker.GetLatestSnapshot(accountId));
        await tracker.PollOnceAsync(CancellationToken.None);

        Assert.Same(snapshot, tracker.GetLatestSnapshot(accountId));
        Assert.Null(tracker.GetLatestSnapshot(Guid.NewGuid()));
    }

    private sealed class FixedTransport(PlayerProgressSnapshot snapshot) : IPlayerProfileTransport
    {
        public Task<IReadOnlyList<RankingEntry>> GetRankingAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RankingEntry>>([]);

        public Task<PlayerProgressSnapshot> GetProfileAsync(int playerId, CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }
}
```

`src/FourFoldAccountManager.Desktop.Tests/Views/ExperienceCalculatorTargetTests.cs`:

```csharp
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Tracking;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class ExperienceCalculatorTargetTests
{
    [Fact]
    public void SavedTargetFillsInWhenSwitchingAccounts() => WpfTestHost.Run(() =>
    {
        var (panel, first, second) = CreatePanel();

        panel.SetSnapshot(first, Snapshot(), savedTargetLevel: 3);
        Assert.Equal("3", panel.TargetLevelBox.Text);
        Assert.Equal("25", panel.RemainingText.Text);

        panel.SetSnapshot(second, Snapshot(), savedTargetLevel: null);
        Assert.Equal(string.Empty, panel.TargetLevelBox.Text);
        Assert.Equal("—", panel.RemainingText.Text);
    });

    [Fact]
    public void TypingATargetSavesItOnceAfterThePause() => WpfTestHost.Run(() =>
    {
        var (panel, first, _) = CreatePanel();
        var saves = new List<(Guid, long?)>();
        panel.TargetLevelChanged += (accountId, level) => saves.Add((accountId, level));
        panel.SetSnapshot(first, Snapshot());

        Assert.Equal(TimeSpan.FromMilliseconds(500), panel.TargetSaveDelay);
        panel.TargetLevelBox.Text = "1";
        panel.TargetLevelBox.Text = "12";
        Assert.Empty(saves);
        panel.FlushPendingTargetSave();

        Assert.Equal([(first.Id, (long?)12)], saves);
    });

    [Fact]
    public void InvalidTextKeepsTheSavedTargetAndClearingRemovesIt() => WpfTestHost.Run(() =>
    {
        var (panel, first, _) = CreatePanel();
        var saves = new List<(Guid, long?)>();
        panel.TargetLevelChanged += (accountId, level) => saves.Add((accountId, level));
        panel.SetSnapshot(first, Snapshot(), savedTargetLevel: 3);

        panel.TargetLevelBox.Text = "abc";
        panel.FlushPendingTargetSave();
        Assert.Empty(saves);

        panel.TargetLevelBox.Text = string.Empty;
        panel.FlushPendingTargetSave();
        Assert.Equal([(first.Id, (long?)null)], saves);
    });

    [Fact]
    public void SwitchingAccountsSavesThePendingTargetForThePreviousAccount() => WpfTestHost.Run(() =>
    {
        var (panel, first, second) = CreatePanel();
        var saves = new List<(Guid, long?)>();
        panel.TargetLevelChanged += (accountId, level) => saves.Add((accountId, level));
        panel.SetSnapshot(first, Snapshot());

        panel.TargetLevelBox.Text = "7";
        panel.SetSnapshot(second, Snapshot(), savedTargetLevel: 4);

        Assert.Equal([(first.Id, (long?)7)], saves);
        Assert.Equal("4", panel.TargetLevelBox.Text);
    });

    [Fact]
    public void FillingInASavedTargetIsNotSavedAgain() => WpfTestHost.Run(() =>
    {
        var (panel, first, _) = CreatePanel();
        var saves = new List<(Guid, long?)>();
        panel.TargetLevelChanged += (accountId, level) => saves.Add((accountId, level));

        panel.SetSnapshot(first, Snapshot(), savedTargetLevel: 3);
        panel.FlushPendingTargetSave();

        Assert.Empty(saves);
    });

    private static (ExperienceCalculatorPanel Panel, AccountProfile First, AccountProfile Second) CreatePanel()
    {
        var panel = new ExperienceCalculatorPanel();
        var first = AccountProfile.Create("First");
        var second = AccountProfile.Create("Second");
        panel.SetAccounts([first, second]);
        return (panel, first, second);
    }

    private static PlayerProgressSnapshot Snapshot() =>
        new("Alice", "Warrior", new Dictionary<string, ClassProfileSnapshot>
        {
            ["Warrior"] = new(2, 5, 30, null) { ClassName = "Warrior" }
        }, []);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release --filter "FullyQualifiedName~XpTrackerSnapshotTests|FullyQualifiedName~ExperienceCalculatorTargetTests"`
Expected: the build FAILS with `'XpTrackerCoordinator' does not contain a definition for 'GetLatestSnapshot'` and `No overload for method 'SetSnapshot' takes 3 arguments`.

- [ ] **Step 3: Expose the tracker's latest snapshot**

In `src/FourFoldAccountManager.Desktop/Services/XpTrackerCoordinator.cs`:

1. After `GetActiveLeaderboardProfiles()`, add:

```csharp
    // The newest profile fetched this session for an open account; null until its first successful fetch.
    public PlayerProgressSnapshot? GetLatestSnapshot(Guid accountId) =>
        _active.TryGetValue(accountId, out var account) ? account.LatestSnapshot : null;
```

2. Change `private async Task PollOnceAsync(CancellationToken cancellationToken)` to `internal async Task PollOnceAsync(CancellationToken cancellationToken)`.
3. In `PollAccountAsync`, directly after `account.Session.ApplySnapshot(profile, sampledAt);`, add:

```csharp
            account.LatestSnapshot = profile;
```

4. In the nested `TrackedAccount` class, add this after `CanRetryProfileRead`:

```csharp
        public PlayerProgressSnapshot? LatestSnapshot { get; set; }
```

- [ ] **Step 4: Fill in and save targets in the sidebar XP calc**

In `src/FourFoldAccountManager.Desktop/Views/ExperienceCalculatorPanel.xaml.cs`:

1. Add `using System.Windows.Threading;`.
2. Add these fields after `_suppressAccountSelection`:

```csharp
    private readonly DispatcherTimer _targetSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private (Guid AccountId, long? TargetLevel)? _pendingTargetSave;
```

3. In the constructor, after `TransitionsControl.ItemsSource = _transitions;`, add:

```csharp
        _targetSaveTimer.Tick += (_, _) => FlushPendingTargetSave();
```

4. After the `AccountSelectionRequested` event, add the event and the test hook:

```csharp
    // Raised once typing pauses; a null level removes the account's saved target.
    public event Action<Guid, long?>? TargetLevelChanged;

    internal TimeSpan TargetSaveDelay => _targetSaveTimer.Interval;
```

5. Replace the whole `SetSnapshot` method with:

```csharp
    public void SetSnapshot(AccountProfile account, PlayerProgressSnapshot snapshot, long? savedTargetLevel = null)
    {
        var sameAccount = _snapshotAccountId == account.Id;
        if (!sameAccount)
        {
            // Save what was typed for the previous account before its text is replaced.
            FlushPendingTargetSave();
        }

        var targetText = sameAccount
            ? TargetLevelBox.Text
            : savedTargetLevel?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _snapshotAccountId = account.Id;
        SetSelectedAccount(account);
        _state = ExperienceCalculatorState.FromSnapshot(snapshot);
        _updatingTarget = true;
        TargetLevelBox.Text = targetText;
        _updatingTarget = false;
        if (long.TryParse(targetText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture,
                out var targetLevel) && targetLevel > 0)
            _state = _state.WithTarget(targetLevel);
        Render();
    }
```

6. In `ClearSnapshot`, add `FlushPendingTargetSave();` as the first line. Then replace the line `TargetLevelBox.Text = string.Empty;` with:

```csharp
        _updatingTarget = true;
        TargetLevelBox.Text = string.Empty;
        _updatingTarget = false;
```

7. Replace the body of `TargetLevelBox_TextChanged` with:

```csharp
        if (!_updatingTarget)
        {
            ApplyTarget();
            ScheduleTargetSave();
        }
```

8. Add these methods after `ApplyTarget`:

```csharp
    private void ScheduleTargetSave()
    {
        _targetSaveTimer.Stop();
        _pendingTargetSave = null;
        if (_snapshotAccountId is not { } accountId)
        {
            return;
        }

        var text = TargetLevelBox.Text.Trim();
        if (text.Length == 0)
        {
            _pendingTargetSave = (accountId, null);
        }
        else if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level > 0)
        {
            _pendingTargetSave = (accountId, level);
        }
        else
        {
            // Invalid text never changes the saved target.
            return;
        }

        _targetSaveTimer.Start();
    }

    internal void FlushPendingTargetSave()
    {
        _targetSaveTimer.Stop();
        if (_pendingTargetSave is not { } pending)
        {
            return;
        }

        _pendingTargetSave = null;
        TargetLevelChanged?.Invoke(pending.AccountId, pending.TargetLevel);
    }
```

- [ ] **Step 5: Pass targets through the sidebar and save them**

In `src/FourFoldAccountManager.Desktop/Views/PluginSidebar.xaml.cs`:

1. In the constructor, after the `XpCalculatorPanelView.AccountSelectionRequested` line, add:

```csharp
        XpCalculatorPanelView.TargetLevelChanged += (accountId, targetLevel) =>
            XpTargetLevelChanged?.Invoke(accountId, targetLevel);
```

2. After `public event EventHandler? RefreshRequested;`, add:

```csharp
    public event Action<Guid, long?>? XpTargetLevelChanged;
```

3. Change `SetProfileSnapshot` to:

```csharp
    public void SetProfileSnapshot(PlayerProgressSnapshot? snapshot, long? savedXpTargetLevel = null)
    {
        if (snapshot is not null && _selectedAccount is not null)
        {
            ClassComparisonPanelView.SetSnapshot(_selectedAccount, snapshot);
            XpCalculatorPanelView.SetSnapshot(_selectedAccount, snapshot, savedXpTargetLevel);
        }
    }
```

In `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`:

1. Add `using FourFoldAccountManager.Core.Calculation;` if it is not already present.
2. In the constructor, after the `PluginSidebar.RefreshRequested += ...` line, add:

```csharp
        PluginSidebar.XpTargetLevelChanged += (accountId, targetLevel) =>
            _ = SaveXpTargetLevelAsync(accountId, targetLevel);
```

3. Replace `PluginSidebar.SetProfileSnapshot(result.Snapshot);` with:

```csharp
                PluginSidebar.SetProfileSnapshot(result.Snapshot, XpCalculatorTargets.Get(_panelSettings, account.Id));
```

4. Add this method after `RefreshSelectedProfileAsync`:

```csharp
    private async Task SaveXpTargetLevelAsync(Guid accountId, long? targetLevel)
    {
        try
        {
            await UpdateSettingsAsync(settings => XpCalculatorTargets.WithTarget(settings, accountId, targetLevel));
            RefreshTrackerRows();
        }
        catch
        {
            GlobalStatusText.Text = "The XP calculator target could not be saved.";
        }
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`
Expected: PASS, including 1 `XpTrackerSnapshotTests`, 5 `ExperienceCalculatorTargetTests`, and the existing `ExperienceCalculatorPanelTests`.

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/FourFoldAccountManager.Desktop src/FourFoldAccountManager.Desktop.Tests
git commit -m "feat: share tracker snapshots and save XP calculator targets from the sidebar"
```

---

### Task 4: Stats and XP calc overlay cards (Desktop)

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Views/OverlayCardText.cs`
- Create: `src/FourFoldAccountManager.Desktop/Views/StatsCardData.cs`
- Create: `src/FourFoldAccountManager.Desktop/Views/XpCalcCardData.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Resources/OverlayCardTemplates.xaml`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs` (`RefreshTrackerRows`, `AddOverlayAddOn`, `CreateOverlayCardData`)
- Create: `src/FourFoldAccountManager.Desktop.Tests/VisualTree.cs`
- Test: `src/FourFoldAccountManager.Desktop.Tests/Views/AccountOverlayCardTests.cs`

**Interfaces:**
- Consumes:
  - Task 2: `StatsCardContent`, `StatsCardRow`, `XpCalcCardContent`, `OverlayAddOnKind.Stats` and `OverlayAddOnKind.XpCalc`.
  - Task 3: `XpTrackerCoordinator.GetLatestSnapshot`.
  - Task 1: `XpCalculatorTargets.Get`.
  - Existing: `IOverlayCardData`, `OverlayCardLayer`, `OverlayCardModel`, `XpTrackerState.IsStale` and `WpfTestHost`.
- Produces:
  - `record StatsCardData(string AccountLabel, bool IsStale, StatsCardContent Content) : IOverlayCardData`, with `HeaderText`, `LineText`, `Rows` and `Summary`.
  - `record XpCalcCardData(string AccountLabel, bool IsStale, XpCalcCardContent Content) : IOverlayCardData`, with `HeaderText`, `LineText` and `Summary`.
  - `internal static class OverlayCardText` with `Header(string accountLabel, string detail, bool isStale)`.
  - Test helper `internal static class VisualTree` with `Descendants<T>(DependencyObject)`.

- [ ] **Step 1: Write the failing tests**

`src/FourFoldAccountManager.Desktop.Tests/VisualTree.cs`:

```csharp
using System.Windows;
using System.Windows.Media;

namespace FourFoldAccountManager.Desktop.Tests;

internal static class VisualTree
{
    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
```

`src/FourFoldAccountManager.Desktop.Tests/Views/AccountOverlayCardTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Tracking;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class AccountOverlayCardTests
{
    private static readonly StatsCardContent StatsContent = StatsCardContent.FromState(
        new ClassComparisonDisplayState(true, "Profile stats loaded.", "Mage", 42, null,
        [
            new ClassStatComparison(CharacterStat.Hp, 1_100, 1_000, 100, 10.0, ComparisonDirection.Above),
            new ClassStatComparison(CharacterStat.Defense, 90, 100, -10, -10.0, ComparisonDirection.Below)
        ], 1, 1, 0, 0.0));

    [Fact]
    public void StatsCardRendersHeaderSummaryAndRows() => WpfTestHost.Run(() =>
    {
        var texts = RenderTexts(OverlayAddOnKind.Stats, new StatsCardData("Alice", false, StatsContent), 1000, 500);

        Assert.Contains("Alice · Mage 42", texts);
        Assert.Contains("▲1 ▼1 · mean 0.0%", texts);
        Assert.Contains("HP", texts);
        Assert.Contains("1,100", texts);
        Assert.Contains("+10.0%", texts);
        Assert.Contains("DEF", texts);
        Assert.Contains("-10.0%", texts);
    });

    [Fact]
    public void XpCalcCardRendersTargetAndRemaining() => WpfTestHost.Run(() =>
    {
        var content = XpCalcCardContent.FromSnapshot(Snapshot(), savedTargetLevel: 3);

        var texts = RenderTexts(OverlayAddOnKind.XpCalc, new XpCalcCardData("Alice", false, content), 1000, 500);

        Assert.Contains("Alice · Warrior 2 → 3", texts);
        Assert.Contains("25 XP · 1 level to go", texts);
    });

    [Fact]
    public void StaleCardsMarkTheirHeader()
    {
        Assert.Equal("Alice · Mage 42 · stale", new StatsCardData("Alice", true, StatsContent).HeaderText);
        var waiting = new XpCalcCardData("Alice", true, XpCalcCardContent.FromSnapshot(null, null));
        Assert.Equal("Alice · stale", waiting.HeaderText);
        Assert.Equal("Waiting for profile…", waiting.LineText);
    }

    [Fact]
    public void SummariesShowCountsAndTarget()
    {
        Assert.Equal("▲1 ▼1", new StatsCardData("Alice", false, StatsContent).Summary);
        Assert.Equal("→ 3", new XpCalcCardData("Alice", false,
            XpCalcCardContent.FromSnapshot(Snapshot(), 3)).Summary);
        Assert.Equal(string.Empty, new XpCalcCardData("Alice", false,
            XpCalcCardContent.FromSnapshot(null, null)).Summary);
        Assert.Equal(string.Empty, new StatsCardData("Alice", false, StatsCardContent.FromSnapshot(null)).Summary);
    }

    [Theory]
    [InlineData(OverlayAddOnKind.Stats)]
    [InlineData(OverlayAddOnKind.XpCalc)]
    public void CardsShrinkToFitAtTheirMinimumSize(OverlayAddOnKind kind) => WpfTestHost.Run(() =>
    {
        OverlayAddOnCatalog.TryGet(kind, out var definition);
        IOverlayCardData data = kind == OverlayAddOnKind.Stats
            ? new StatsCardData("Alice", false, StatsContent)
            : new XpCalcCardData("Alice", false, XpCalcCardContent.FromSnapshot(Snapshot(), 3));
        var (layer, key) = RenderCard(kind, data, definition!.MinimumWidth, definition.MinimumHeight);

        var frame = layer.Frames[key];
        var viewbox = Assert.Single(VisualTree.Descendants<Viewbox>(frame));
        var child = Assert.IsAssignableFrom<FrameworkElement>(viewbox.Child);

        Assert.True(viewbox.ActualWidth <= frame.ActualWidth && viewbox.ActualHeight <= frame.ActualHeight);
        Assert.True(viewbox.ActualWidth < child.DesiredSize.Width || viewbox.ActualHeight < child.DesiredSize.Height,
            $"Expected the {kind} card content ({child.DesiredSize}) to shrink into {viewbox.ActualWidth}x{viewbox.ActualHeight}.");
    });

    private static string[] RenderTexts(OverlayAddOnKind kind, IOverlayCardData data, double width, double height)
    {
        var (layer, key) = RenderCard(kind, data, width, height);
        return VisualTree.Descendants<TextBlock>(layer.Frames[key]).Select(text => text.Text).ToArray();
    }

    private static (OverlayCardLayer Layer, OverlayCardKey Key) RenderCard(
        OverlayAddOnKind kind, IOverlayCardData data, double width, double height)
    {
        OverlayAddOnCatalog.TryGet(kind, out var definition);
        var layer = new OverlayCardLayer { Width = width, Height = height };
        layer.Measure(new Size(width, height));
        layer.Arrange(new Rect(0, 0, width, height));
        layer.UpdateLayout();
        var key = new OverlayCardKey(kind, Guid.NewGuid());
        layer.SetCards([new OverlayCardModel(key, definition!, null, 0, data)], editing: false);
        layer.UpdateLayout();
        return (layer, key);
    }

    private static PlayerProgressSnapshot Snapshot() =>
        new("Alice", "Warrior", new Dictionary<string, ClassProfileSnapshot>
        {
            ["Warrior"] = new(2, 5, 30, null) { ClassName = "Warrior" }
        }, []);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release --filter "FullyQualifiedName~AccountOverlayCardTests"`
Expected: the build FAILS with `The type or namespace name 'StatsCardData' could not be found`.

- [ ] **Step 3: Add the card data records**

`src/FourFoldAccountManager.Desktop/Views/OverlayCardText.cs`:

```csharp
namespace FourFoldAccountManager.Desktop.Views;

internal static class OverlayCardText
{
    // "Alice · Mage 42 · stale": empty parts are left out.
    public static string Header(string accountLabel, string detail, bool isStale) =>
        string.Join(" · ", new[] { accountLabel, detail, isStale ? "stale" : string.Empty }
            .Where(part => part.Length > 0));
}
```

`src/FourFoldAccountManager.Desktop/Views/StatsCardData.cs`:

```csharp
using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record StatsCardData(string AccountLabel, bool IsStale, StatsCardContent Content) : IOverlayCardData
{
    public string HeaderText => OverlayCardText.Header(AccountLabel, Content.Header, IsStale);

    public string LineText => Content.IsAvailable ? Content.SummaryText : Content.Status;

    public IReadOnlyList<StatsCardRow> Rows => Content.Rows;

    public string Summary => Content.CountsText;
}
```

`src/FourFoldAccountManager.Desktop/Views/XpCalcCardData.cs`:

```csharp
using System.Globalization;
using FourFoldAccountManager.Core.Overlay;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record XpCalcCardData(string AccountLabel, bool IsStale, XpCalcCardContent Content) : IOverlayCardData
{
    public string HeaderText => OverlayCardText.Header(AccountLabel, Content.TargetLine, IsStale);

    public string LineText => Content.IsAvailable ? Content.RemainingLine : Content.Status;

    public string Summary => Content.TargetLevel is { } target
        ? string.Create(CultureInfo.InvariantCulture, $"→ {target}")
        : string.Empty;
}
```

- [ ] **Step 4: Add the card templates**

In `src/FourFoldAccountManager.Desktop/Resources/OverlayCardTemplates.xaml`, add these templates before `</ResourceDictionary>`:

```xml
    <DataTemplate DataType="{x:Type views:StatsCardData}">
        <Viewbox Stretch="Uniform" StretchDirection="DownOnly" HorizontalAlignment="Left" VerticalAlignment="Top">
            <StackPanel Width="232">
                <TextBlock Text="{Binding HeaderText}" FontSize="11" FontWeight="SemiBold"
                           Foreground="{DynamicResource Brush.TextSecondary}" TextTrimming="CharacterEllipsis" />
                <TextBlock Text="{Binding LineText}" FontSize="13" FontWeight="Bold" Margin="0,2,0,6"
                           Foreground="{DynamicResource Brush.AccentGold}" TextWrapping="Wrap" />
                <ItemsControl ItemsSource="{Binding Rows}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Grid Margin="0,1,0,0">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="40" />
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="64" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Text="{Binding StatLabel}" FontSize="11"
                                           Foreground="{DynamicResource Brush.TextMuted}" />
                                <TextBlock Grid.Column="1" Text="{Binding ProfileText}" FontSize="11"
                                           HorizontalAlignment="Right" Typography.NumeralAlignment="Tabular"
                                           Foreground="{DynamicResource Brush.TextPrimary}" />
                                <TextBlock Grid.Column="2" Text="{Binding AverageText}" FontSize="11"
                                           HorizontalAlignment="Right" Typography.NumeralAlignment="Tabular"
                                           Foreground="{DynamicResource Brush.TextMuted}" />
                                <TextBlock Grid.Column="3" Text="{Binding DeltaText}" FontSize="11"
                                           HorizontalAlignment="Right" Typography.NumeralAlignment="Tabular">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock">
                                            <Setter Property="Foreground" Value="{DynamicResource Brush.TextMuted}" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding Direction}" Value="Above">
                                                    <Setter Property="Foreground" Value="{DynamicResource Brush.AccentGold}" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </Viewbox>
    </DataTemplate>
    <DataTemplate DataType="{x:Type views:XpCalcCardData}">
        <Viewbox Stretch="Uniform" StretchDirection="DownOnly" HorizontalAlignment="Left">
            <Grid VerticalAlignment="Center">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>
                <TextBlock Text="{Binding HeaderText}" FontSize="10" FontWeight="SemiBold"
                           Foreground="{DynamicResource Brush.TextSecondary}" />
                <TextBlock Grid.Row="1" Text="{Binding LineText}" FontSize="14" FontWeight="Bold"
                           Typography.NumeralAlignment="Tabular" Foreground="{DynamicResource Brush.AccentGold}" />
            </Grid>
        </Viewbox>
    </DataTemplate>
```

- [ ] **Step 5: Supply card data for Stats and XP calc in MainWindow**

In `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`:

1. In `RefreshTrackerRows`, inside `if (account is not null)`, add this line before `var switches = new List<OverlayTraySwitch>();`:

```csharp
                    var isStale = states.TryGetValue(trackedAccountId, out var trackerState) && trackerState.IsStale;
```

2. In the same block, change the per-account call to:

```csharp
                        AddOverlayAddOn(definition, new OverlayCardKey(definition.Kind, trackedAccountId),
                            label, trackerRow, isStale, cards, switches);
```

3. Change the global call to:

```csharp
            AddOverlayAddOn(definition, new OverlayCardKey(definition.Kind, null),
                string.Empty, null, false, globalCards, globalSwitches);
```

4. In `AddOverlayAddOn`, add the parameter `bool isStale,` after `XpTrackerRow? trackerRow,`, and change its first statement to:

```csharp
        if (CreateOverlayCardData(definition.Kind, key.AccountId, accountLabel, trackerRow, isStale) is not { } data)
```

5. Replace the whole `CreateOverlayCardData` method with:

```csharp
    // Each overlay add-on supplies its card data here; a kind without data is not offered in the Overlays panel.
    private IOverlayCardData? CreateOverlayCardData(
        OverlayAddOnKind kind, Guid? accountId, string accountLabel, XpTrackerRow? trackerRow, bool isStale) =>
        kind switch
        {
            OverlayAddOnKind.Xp => new XpOverlayCardData(accountLabel, trackerRow?.XpPerHourText ?? "— XP/hr"),
            OverlayAddOnKind.Stats when accountId is { } statsAccountId => new StatsCardData(accountLabel, isStale,
                StatsCardContent.FromSnapshot(_xpTracker.GetLatestSnapshot(statsAccountId))),
            OverlayAddOnKind.XpCalc when accountId is { } calcAccountId => new XpCalcCardData(accountLabel, isStale,
                XpCalcCardContent.FromSnapshot(_xpTracker.GetLatestSnapshot(calcAccountId),
                    XpCalculatorTargets.Get(_panelSettings, calcAccountId))),
            OverlayAddOnKind.Timer => _timer.Display,
            _ => null
        };
```

`MainWindow.xaml.cs` already has `using FourFoldAccountManager.Core.Overlay;`, and `FourFoldAccountManager.Core.Calculation` since Task 3.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`
Expected: PASS, including 6 `AccountOverlayCardTests` cases.

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/FourFoldAccountManager.Desktop src/FourFoldAccountManager.Desktop.Tests
git commit -m "feat: add stats and XP calc full-screen overlay cards"
```

---

### Task 5: Timer tab card container, README, and test build

**Files:**
- Modify: `src/FourFoldAccountManager.Desktop/Views/TimerPanel.xaml`
- Modify: `README.md`
- Test: `src/FourFoldAccountManager.Desktop.Tests/Views/TimerPanelTests.cs`

**Interfaces:**
- Consumes: the existing `TimerPanel`; `SidebarCardStyle` in `Resources/Theme.xaml`.
- Produces: new internal named elements `TimerCard` (Border), `HeadingLabel` and `HeadingTitle` (TextBlocks) in `TimerPanel`.

- [ ] **Step 1: Write the failing test**

Add this test inside the class in `src/FourFoldAccountManager.Desktop.Tests/Views/TimerPanelTests.cs`:

```csharp
    [Fact]
    public void TimerTabSitsInTheSidebarCardWithItsHeading() => WpfTestHost.Run(() =>
    {
        var panel = new TimerPanel();

        Assert.Same(panel.TimerCard, panel.Content);
        Assert.Same(Application.Current.FindResource("SidebarCardStyle"), panel.TimerCard.Style);
        Assert.Equal(new Thickness(14), panel.TimerCard.Padding);
        Assert.Equal("TIMER", panel.HeadingLabel.Text);
        Assert.Equal("Speedrun stopwatch", panel.HeadingTitle.Text);
    });
```

The file already has `using System.Windows;`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release --filter "FullyQualifiedName~TimerTabSitsInTheSidebarCard"`
Expected: the build FAILS with `'TimerPanel' does not contain a definition for 'TimerCard'`.

- [ ] **Step 3: Wrap the Timer tab in the sidebar card**

In `src/FourFoldAccountManager.Desktop/Views/TimerPanel.xaml`, wrap the existing root `<Grid>` without changing it.

1. Directly before the root `<Grid>` (the first `<Grid>` after the `<UserControl ...>` tag), insert:

```xml
    <Border x:Name="TimerCard" Style="{StaticResource SidebarCardStyle}" Padding="14">
        <DockPanel>
            <StackPanel DockPanel.Dock="Top" Margin="2,2,2,16">
                <TextBlock x:Name="HeadingLabel" Text="TIMER" FontSize="10" FontWeight="Bold"
                           Foreground="{DynamicResource Brush.AccentGold}" />
                <TextBlock x:Name="HeadingTitle" Text="Speedrun stopwatch" FontSize="16" FontWeight="SemiBold"
                           Margin="0,5,0,0" />
            </StackPanel>
```

2. Directly after that root Grid's closing `</Grid>` (the one just before `</UserControl>`), insert:

```xml
        </DockPanel>
    </Border>
```

The Grid's rows, including the star-sized lap list row, now fill the rest of the `DockPanel`.

- [ ] **Step 4: Document the new cards and the collapsible sidebar**

In `README.md`, replace:

```
switch each account's XP/hr card on or off, and drag cards into place.
```

with:

```
switch each account's XP/hr, Stats, and XP calc cards on or off, and drag cards into place. The Stats card shows the active class's full stat comparison, and the XP calc card shows the XP and levels left to the target saved in the sidebar XP calc (or the next level). Both use the XP tracker's once-a-minute profile reads, so they add no extra requests. The button at the right of the toolbar collapses the Plugins sidebar; FourFold remembers the choice.
```

- [ ] **Step 5: Build and run every suite**

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: PASS.

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`
Expected: PASS, including `TimerTabSitsInTheSidebarCardWithItsHeading`.

Run: `dotnet test src/FourFoldAccountManager.Leaderboard.Tests -c Release --filter "FullyQualifiedName!~LeaderboardStoreTests&FullyQualifiedName!~Readiness"`
Expected: PASS. This is a regression check only.

- [ ] **Step 6: Publish a test EXE for the manual check**

```bash
dotnet publish src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "$TEMP/fourfold-cards-test"
```

The user performs these checks. Report them as not performed if you cannot drive the GUI.

1. In full screen, switch **Stats** and **XP calc** on for two clients. Each card shows its own account and class. Before the first tracker read, a card shows "Waiting for profile…".
2. Set different targets in the sidebar XP calc for each account, then switch accounts. Each account's target fills in, and each XP calc card shows "→ target".
3. Wait about a minute. The cards refresh.
4. Shrink a Stats card to its smallest size. The full table is still visible, just smaller.
5. The Timer tab now sits in the same framed card, with a heading, as the other tabs.
6. Collapse the Plugins sidebar and restart the app. It stays collapsed.

- [ ] **Step 7: Commit**

```bash
git add src/FourFoldAccountManager.Desktop/Views/TimerPanel.xaml src/FourFoldAccountManager.Desktop.Tests README.md
git commit -m "feat: frame the timer tab like the other plugins and document the new cards"
```
