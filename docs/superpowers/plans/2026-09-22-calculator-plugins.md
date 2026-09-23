# FourFold Calculator Plugins Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a reusable Plugins sidebar containing XP Tracker, live base-stat Class Comparison, and target-level XP Calculator tools backed by the existing public FourFold profile flow.

**Architecture:** Extend the existing `PlayerProfileHtmlParser`/`XpTrackerCoordinator` pipeline so one verified public profile fetch produces all tracked-class progression, base-stat, update, and equipment metadata. Keep class-average comparison and XP-curve math in the Core project, then render them through three native WPF plugin views inside a right-side host that owns selected-account refresh lifecycle.

**Tech Stack:** .NET 10, C# nullable records, WPF/XAML, AngleSharp 1.8.2, WebView2 account sessions, xUnit Core tests.

**Spec:** `docs/superpowers/specs/2026-09-22-calculator-plugins-design.md`

## Global Constraints

- Use the existing public `player.php?id=<resolved-id>` profile path; WebView DOM extraction is not part of this feature.
- A username is sufficient for normal top-200 unique matches; manual profile ID/URL remains required only for ambiguous or out-of-ranking users.
- The active-class profile block is the authoritative base-stat block; equipment names are descriptive metadata and never change comparison values.
- The class-average catalog is the only intentionally static data: it contains the 16 class base/growth definitions from the supplied calculator source. The selected account's active class and all displayed profile values are resolved dynamically from `PlayerProgressSnapshot.ActiveClassName` and its matching `ClassProfileSnapshot`.
- Class Comparison includes only HP, SP, ATT, MAG, SKL, SPD, LCK, DEF, and RES versus projected class averages.
- XP Calculator uses `5 * level * (level + 1)` for the next-level cap and `(5 / 3) * (level^3 - level)` for cumulative XP.
- Do not add calculator stats to `AccountProfile` or the persisted account JSON schema.
- Keep full-screen hiding and XP overlay behavior intact; the Plugins sidebar is hidden in full screen.
- Use the existing `Brush.*` palette resources; do not introduce a separate HTML calculator palette.
- Preserve existing XP Tracker rate, reset, link, and stored-interval behavior.
- Force-add only the ignored spec/plan files under `docs/`; do not change the repository-wide ignore policy.

## Review Focus

- A profile card containing comma-separated values and `HP current / max` or `SP current / max` must parse maximum HP/SP and all eight other base stats without truncation.
- A profile with an optional/missing equipment line must remain usable for XP and stat comparison; a missing required stat must make Class Comparison unavailable without breaking XP tracking for other valid classes.
- A profile with no active-class badge, duplicate class, invalid EXP cap, or username mismatch must produce a typed unavailable/mismatch result instead of silently selecting the wrong class.
- A username outside the top 200 or matching multiple ranking rows must preserve the current manual-link guidance; a unique top-200 match must resolve and cache its profile ID without user input.
- A target level equal to current level must return zero, a lower target must be rejected, and current XP progress must be subtracted from the target threshold without overflow.
- An asynchronous refresh completing after the user selects another account must not paint the old profile into the new account’s calculator view.

---

### Task 1: Add Core test infrastructure and parse the full public profile

**Files:**
- Create: `tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj`
- Create: `tests/FourFoldAccountManager.Core.Tests/Fixtures/player-profile-sample.html`
- Create: `tests/FourFoldAccountManager.Core.Tests/PlayerProfileHtmlParserTests.cs`
- Modify: `FourFoldAccountManager.sln`
- Create: `src/FourFoldAccountManager.Core/Tracking/ClassProfileSnapshot.cs`
- Modify: `src/FourFoldAccountManager.Core/Tracking/PlayerProgressSnapshot.cs`
- Modify: `src/FourFoldAccountManager.Core/Tracking/PlayerProfileHtmlParser.cs`
- Delete: `src/FourFoldAccountManager.Core/Tracking/ClassXpSnapshot.cs` after all references are migrated

**Interfaces:**
- Produces `ClassProfileSnapshot` with `Level`, `CurrentXp`, `NextLevelXp`, `SourceUpdated`, nullable `Hp`, `Sp`, `Attack`, `Magic`, `Skill`, `Speed`, `Defense`, `Resistance`, `Luck`, and `Equipment`.
- Produces `PlayerProgressSnapshot.Classes` as `IReadOnlyDictionary<string, ClassProfileSnapshot>` while retaining `Username`, `ActiveClassName`, and `InvalidClasses` compatibility for XP tracking.
- Adds `InvalidStatClasses` to `PlayerProgressSnapshot`; XP-invalid classes remain in `InvalidClasses`, while stat-invalid classes do not poison XP deltas for valid classes.

- [ ] **Step 1: Add the test project, fixture, and failing parser test.** Create a `net10.0` xUnit project referencing Core with `Microsoft.NET.Test.Sdk` 17.13.0, `xunit` 2.9.3, and `xunit.runner.visualstudio` 3.0.2 with `PrivateAssets=all`; add it to the solution. Create a fixture with two `.class-grid .class-card .class-body` entries matching the live profile shape: an active `Arctic Soldier` card with `Level 278`, `EXP 270,060 / 387,810`, `HP 22,130 / 22,130`, `SP 998 / 998`, `ATT 238`, `MAG 61`, `SKL 179`, `SPD 75`, `DEF 216`, `RES 30`, `LCK 75`, `Updated`, `Armor`, `Helmet`, `Hair`, and `Weapon`; and an inactive `Charmer` card with level-1 values and no active badge. Assert the desired typed contract:

```csharp
[Fact]
public void Parse_reads_full_base_stats_and_equipment_for_each_class()
{
    var snapshot = PlayerProfileHtmlParser.Parse(ReadFixture("player-profile-sample.html"));

    Assert.Equal("Dweebstify", snapshot.Username);
    Assert.Equal("Arctic Soldier", snapshot.ActiveClassName);

    var active = snapshot.Classes["Arctic Soldier"];
    Assert.Equal(278, active.Level);
    Assert.Equal(270_060L, active.CurrentXp);
    Assert.Equal(387_810L, active.NextLevelXp);
    Assert.Equal(22_130L, active.Hp);
    Assert.Equal(998L, active.Sp);
    Assert.Equal(238L, active.Attack);
    Assert.Equal(61L, active.Magic);
    Assert.Equal(179L, active.Skill);
    Assert.Equal(75L, active.Speed);
    Assert.Equal(216L, active.Defense);
    Assert.Equal(30L, active.Resistance);
    Assert.Equal(75L, active.Luck);
    Assert.Equal("Enhanced Arcticsoldier", active.Equipment["Armor"]);

    Assert.Equal(1, snapshot.Classes["Charmer"].Level);
    Assert.Empty(snapshot.InvalidStatClasses);
}
```

- [ ] **Step 2: Run the focused test and verify it fails for the missing full-stat contract.**

Run: `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~PlayerProfileHtmlParserTests.Parse_reads_full_base_stats_and_equipment_for_each_class`

Expected: FAIL/compile failure because the new record properties and test project do not exist yet. Do not implement parser code before observing this failure.

- [ ] **Step 3: Implement the full profile records and parser.** Replace the four-field `ClassXpSnapshot` record with `ClassProfileSnapshot`, keeping the first four constructor fields unchanged so old tracker JSON can deserialize missing optional stat properties as null. Extend `ReadMeta` usage for `HP`, `SP`, `ATT`, `MAG`, `SKL`, `SPD`, `DEF`, `RES`, `LCK`, `Armor`, `Helmet`, `Hair`, and `Weapon`. Parse the value before `/` for HP/SP, strip commas, require nonnegative stats, preserve optional equipment as an empty read-only dictionary, and add a class to `InvalidStatClasses` when required stat fields are malformed or missing. Keep level/EXP validation and `InvalidClasses` behavior intact.

- [ ] **Step 4: Add parser edge-case tests and run the focused suite.** Add tests for comma-free/zero values, missing equipment, malformed HP/SP pairs, duplicate classes, missing active-class badge, invalid EXP caps, and old JSON-shaped snapshots deserializing with null optional stats. Run the focused parser test class and expect PASS.

- [ ] **Step 5: Run all Core tests and commit.**

Run: `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj`

Expected: PASS with the new parser tests and no regressions in existing Core behavior.

Commit: `git add FourFoldAccountManager.sln tests src/FourFoldAccountManager.Core/Tracking; git commit -m "feat: parse full player profile stats"`

### Task 2: Share public-profile resolution between XP Tracker and plugins

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Services/PlayerProfileService.cs`
- Create: `src/FourFoldAccountManager.Desktop/Services/PlayerProfileReadResult.cs`
- Create: `src/FourFoldAccountManager.Desktop/Services/IPlayerProfileTransport.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Services/FourFoldRankingClient.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Services/XpTrackerCoordinator.cs`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`
- Create: `tests/FourFoldAccountManager.Core.Tests/RankingIdentityResolverTests.cs`
- Create: `tests/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj`
- Create: `tests/FourFoldAccountManager.Desktop.Tests/PlayerProfileServiceTests.cs`
- Modify: `FourFoldAccountManager.sln`

**Interfaces:**
- Produces `PlayerProfileReadResult` with `Status`, `PlayerId`, `PlayerProgressSnapshot? Snapshot`, and a UI-safe `Message`.
- Produces `PlayerProfileService.ReadAsync(Guid accountId, string? username, int? playerId, CancellationToken)` that first uses a supplied/cached ID, otherwise fetches ranking rows and resolves one exact username match.
- `IPlayerProfileTransport` exposes cancellation-aware ranking and profile fetch methods so service tests can use a fake transport without making network calls.
- `XpTrackerCoordinator` consumes the shared service for polling and retains ownership of XP baseline/interval persistence; the service owns only profile lookup/cache state.

- [ ] **Step 1: Add identity-resolution and service tests.** Cover one exact top-200 match, case-insensitive matching, ambiguous duplicate names, no match, and a supplied positive player ID bypassing ranking lookup. Assert that only ambiguous/no-match cases produce the existing manual-link statuses. Create the Windows desktop test project before writing service tests: target `net10.0-windows10.0.17763.0`, set `UseWPF=true` and `EnableWindowsTargeting=true`, reference Desktop, and use the same pinned xUnit package versions as the Core test project. Add `PlayerProfileServiceTests` with a fake `IPlayerProfileTransport` for successful reads, mismatch, cache, and failure behavior.

- [ ] **Step 2: Run the identity tests to verify the new service contract is absent.**

Run: `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~RankingIdentityResolverTests`

Run: `dotnet test tests/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj --filter FullyQualifiedName~PlayerProfileServiceTests`

Expected: FAIL for the new result/status assertions before the shared lookup contract exists.

- [ ] **Step 3: Implement the shared lookup service.** Define `IPlayerProfileTransport.GetRankingAsync(CancellationToken)` and `GetProfileAsync(int playerId, CancellationToken)`, then adapt `FourFoldRankingClient` to implement it. Resolve the effective username exactly as current XP Tracker does: explicit `RankingUsername`, otherwise saved-login username supplied by `MainWindow`; use a verified `RankingPlayerId` first; otherwise resolve the ranking hyperlink ID; fetch `player.php?id=<id>`; verify returned `Snapshot.Username` matches the requested username; cache the ID and latest snapshot by account ID. Return bounded statuses for missing username, ranking unavailable, ambiguous, not in top 200, profile mismatch, malformed profile, and success.

- [ ] **Step 4: Refactor XP Tracker to consume the service without changing tracking math.** Inject the shared service into `XpTrackerCoordinator`; replace its private identity/profile fetch block with one `ReadAsync` call. Keep `XpTrackingSession.ApplySnapshot`, invalid-class XP handling, stored intervals, reset actions, and status text unchanged. Persist the resolved player ID through the existing `XpStoredAccount` path so a restart can bypass ranking resolution when possible. Route `VerifyPlayerAsync` through the same profile fetch and username check.

- [ ] **Step 5: Add lifecycle tests for the shared result.** Verify a late failed request does not replace a successful cached snapshot, a username mismatch clears the resolved ID, and removing an account invalidates its cache. Use a fake profile transport behind the service so tests never call the network.

- [ ] **Step 6: Run the complete test suite, build the desktop project, and commit.**

Run: `dotnet test FourFoldAccountManager.sln --no-restore`

Run: `dotnet build FourFoldAccountManager.sln --no-restore -c Debug`

Expected: PASS/Build succeeded with no warnings introduced.

Commit: `git add src/FourFoldAccountManager.Desktop/Services src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs tests; git commit -m "feat: share public profile lookup"`

### Task 3: Add class-average comparison and XP-curve Core engines

**Files:**
- Create: `src/FourFoldAccountManager.Core/Calculation/CharacterStat.cs`
- Create: `src/FourFoldAccountManager.Core/Calculation/ClassAverageDefinition.cs`
- Create: `src/FourFoldAccountManager.Core/Calculation/ClassAverageCatalog.cs`
- Create: `src/FourFoldAccountManager.Core/Calculation/ClassComparisonCalculator.cs`
- Create: `src/FourFoldAccountManager.Core/Calculation/ClassComparisonResult.cs`
- Create: `src/FourFoldAccountManager.Core/Calculation/ExperienceCurve.cs`
- Create: `src/FourFoldAccountManager.Core/Calculation/ExperienceProjection.cs`
- Create: `tests/FourFoldAccountManager.Core.Tests/ClassComparisonCalculatorTests.cs`
- Create: `tests/FourFoldAccountManager.Core.Tests/ExperienceCurveTests.cs`

**Interfaces:**
- `CharacterStat` values are `Hp`, `Sp`, `Attack`, `Magic`, `Skill`, `Speed`, `Luck`, `Defense`, `Resistance` in that order.
- `ClassAverageCatalog.TryGet(string className, out ClassAverageDefinition definition)` returns the 16 exact class entries from the supplied HTML source.
- `ClassComparisonCalculator.Compare(ClassProfileSnapshot profile)` returns nine `ClassStatComparison` rows plus above/below counts and mean percentage difference, or an unavailable result with a reason.
- `ExperienceCurve.TotalXpAtLevel(long level)`, `ExperienceCurve.XpForNextLevel(long level)`, and `ExperienceCurve.Project(ClassProfileSnapshot profile, long targetLevel)` use `BigInteger` internally and return `ExperienceProjection` with current/target totals, remaining XP, and transition rows.

- [ ] **Step 1: Write failing comparison tests.** Assert that an Arctic Soldier profile at level 278 produces rounded projected averages from the exact catalog, compares profile values in the required nine-stat order, calculates `profile - average`, assigns above/below/equal direction, and reports counts/mean percentage. Add tests for unknown class, null stat, zero average, and non-finite values returning unavailable.

- [ ] **Step 2: Run the comparison tests and verify they fail.**

Run: `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~ClassComparisonCalculatorTests`

Expected: FAIL because the calculation namespace, catalog, and result types do not exist.

- [ ] **Step 3: Implement the stat catalog and comparison engine.** Port only the `classData` base/growth values from `C:\Users\Cody\Desktop\assests\fourfold-calculator-tight.html`; preserve all 16 class names and seasons as the static reference catalog required to calculate averages. Do not hard-code the user's active class or profile stats: select `profile.ActiveClassName` at runtime and read its matching `ClassProfileSnapshot`. Compute `base + (level - 1) * growth`, apply `Math.Round` before display/comparison, compute percentage only when average is nonzero, and return explicit direction values rather than UI color strings.

- [ ] **Step 4: Write failing XP-curve tests.** Pin the formula with exact values:

```csharp
Assert.Equal(BigInteger.Zero, ExperienceCurve.TotalXpAtLevel(1));
Assert.Equal(new BigInteger(10), ExperienceCurve.TotalXpAtLevel(2));
Assert.Equal(new BigInteger(26_000), ExperienceCurve.TotalXpAtLevel(25));
Assert.Equal(new BigInteger(59_840), ExperienceCurve.TotalXpAtLevel(33));
Assert.Equal(new BigInteger(1_666_500), ExperienceCurve.TotalXpAtLevel(100));
Assert.Equal(new BigInteger(33_840), ExperienceCurve.XpBetweenLevels(25, 33));
```

Also assert that a level-25 profile with `CurrentXp=1,000` returns `32,840` remaining XP to level 33, same-level targets return zero, lower targets are invalid, and a large target cannot silently overflow.

- [ ] **Step 5: Run the XP tests and verify they fail.**

Run: `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~ExperienceCurveTests`

Expected: FAIL because the XP-curve types do not exist.

- [ ] **Step 6: Implement the XP curve.** Use `BigInteger` for `5 * (level^3 - level) / 3`, validate positive levels and target ordering, validate `CurrentXp >= 0 && CurrentXp < NextLevelXp`, verify `NextLevelXp == 5 * level * (level + 1)`, subtract current progress only when valid, and otherwise mark `UsedCurrentProgress=false` with the level-start explanation. Generate one transition row per level from current to target.

- [ ] **Step 7: Run the full Core suite and commit.**

Run: `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj`

Expected: PASS for parser, identity, comparison, and XP curve coverage.

Commit: `git add src/FourFoldAccountManager.Core/Calculation tests/FourFoldAccountManager.Core.Tests; git commit -m "feat: add class comparison and xp math"`

### Task 4: Build the reusable Plugins sidebar and migrate XP Tracker into it

**Files:**
- Create: `src/FourFoldAccountManager.Core/Models/PluginKind.cs`
- Create: `src/FourFoldAccountManager.Core/Panel/PluginSelectionPolicy.cs`
- Create: `src/FourFoldAccountManager.Desktop/Views/PluginSidebar.xaml`
- Create: `src/FourFoldAccountManager.Desktop/Views/PluginSidebar.xaml.cs`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`
- Modify: `src/FourFoldAccountManager.Core/Panel/TrackerPanelPolicy.cs` or create `PluginSidebarPolicy.cs`

**Interfaces:**
- `PluginKind` values are `XpTracker`, `ClassComparison`, and `XpCalculator`; `PluginSelectionPolicy.Normalize` returns `XpTracker` for an undefined value.
- `PluginSidebar` exposes `ActivePlugin`, `SelectedAccountId`, `SetProfileSnapshot(PlayerProgressSnapshot?)`, `SetProfileStatus(...)`, and refresh/link/reset events.
- The sidebar hosts the existing `XpTrackerPanel`, a Class Comparison placeholder, and an XP Calculator placeholder without duplicating right-column visibility logic in MainWindow.

- [ ] **Step 1: Add a pure plugin-selection policy test.** Assert that the default plugin is XP Tracker, setting each known plugin selects only that plugin, and an unknown value falls back to XP Tracker. Keep this state independent of WPF controls.

- [ ] **Step 2: Run the focused policy test and verify it fails.**

Run: `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~PluginSelection`

Expected: FAIL because the plugin selection contract does not exist.

- [ ] **Step 3: Implement the sidebar shell.** Replace the direct `XpTrackerPanel` child in `MainWindow.xaml` with `PluginSidebar`. Use existing `SidebarCardStyle`, `Brush.AccentGold`, `Brush.SurfaceRaised`, and `Brush.Border`; render three compact buttons above a `ContentControl`; preserve XP Tracker context-menu and link events by forwarding them unchanged.

- [ ] **Step 4: Move visibility and full-screen wiring.** Rename the MainWindow references to `PluginSidebar`, keep the current 240-pixel column and 16-pixel gap, hide the sidebar when `_isFullScreen`, and show it when a selected account or open tracked account exists so public-profile calculators work even before a game view is launched. Leave `FullscreenXpOverlayTray` and overlay account tracking untouched. Update `EnterFullScreen`, `ExitFullScreen`, account close, and tracker refresh paths to call one `UpdatePluginSidebarVisibility` method.

- [ ] **Step 5: Wire XP Tracker through the host and run a build smoke test.** Ensure XP Tracker remains the initial active plugin, its row source is still `_xpTrackerRows`, and Link/Reset Rate/Reset All events call the same MainWindow handlers. Run `dotnet build FourFoldAccountManager.sln --no-restore -c Debug` and manually verify the host appears beside the workspace only when not full-screen.

- [ ] **Step 6: Run tests and commit.**

Run: `dotnet test FourFoldAccountManager.sln --no-restore`

Commit: `git add src/FourFoldAccountManager.Core/Panel src/FourFoldAccountManager.Desktop/MainWindow.xaml src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs src/FourFoldAccountManager.Desktop/Views tests; git commit -m "feat: add plugins sidebar"`

### Task 5: Implement the live Class Comparison plugin

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Views/ClassComparisonPanel.xaml`
- Create: `src/FourFoldAccountManager.Desktop/Views/ClassComparisonPanel.xaml.cs`
- Create: `src/FourFoldAccountManager.Desktop/Views/ClassComparisonRow.cs`
- Create: `src/FourFoldAccountManager.Core/Calculation/ClassComparisonDisplayState.cs`
- Create: `tests/FourFoldAccountManager.Core.Tests/ClassComparisonDisplayStateTests.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Views/PluginSidebar.xaml.cs`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`
- Modify: `src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj`
- Create: `src/FourFoldAccountManager.Desktop/Assets/Calculator/classes/*.png` copied from `C:\Users\Cody\Desktop\assests\fourfold_assets\classes`

**Interfaces:**
- `ClassComparisonPanel.SetSnapshot(AccountProfile account, PlayerProgressSnapshot snapshot)` resolves and renders the selected account’s current `ActiveClassName` only; it does not assume a particular class such as Arctic Soldier.
- `ClassComparisonPanel.ClearSnapshot(string status)` clears old-account data and shows an empty/loading/unavailable message.
- `ClassComparisonPanel.RefreshRequested` tells MainWindow to request the selected account’s public profile through `PlayerProfileService`.

- [ ] **Step 1: Add the view-model mapping test.** Create a small pure `ClassComparisonDisplayState` mapper in Core and assert it maps the active class to nine rows in the required order, preserves above/below/equal direction tokens and the profile timestamp/status, and creates an unavailable state for an invalid active class. The WPF panel maps direction tokens to existing brushes; the Core mapper does not reference WPF.

- [ ] **Step 2: Run the mapper test and verify it fails.**

Run: `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~ClassComparisonView`

Expected: FAIL because the mapping contract does not exist.

- [ ] **Step 3: Implement the native panel.** Add account/class/level header, timestamp, refresh button, summary cards for above/below/mean difference, and a scrollable table with `Profile`, `Class average`, `Difference`, and `%`. Use `TextBlock`/`Border` styles from `Theme.xaml`; positive values use gold/green, negative values use danger, neutral values use muted text. Display the matching portrait from `Assets/Calculator/classes`, falling back to the existing profile-initial/monogram pattern.

- [ ] **Step 4: Wire selected-account refresh lifecycle.** On `AccountsListBox_SelectionChanged`, cancel the prior profile request, clear the panel, and if Class Comparison or XP Calculator is active start one `PlayerProfileService.ReadAsync` call. Pass the effective ranking username from the account’s explicit setting or saved credential. Ignore a result whose request generation/account ID no longer matches the current selection. On success, pass the full snapshot to the host; on failure, show the typed status without erasing a valid same-account snapshot.

- [ ] **Step 5: Add resource entries and build.** Copy the 16 class PNGs, add `<Resource Include="Assets\Calculator\classes\*.png" />` to the desktop project, and use pack-relative source paths. Run the full solution build and inspect the panel at the minimum window size to confirm the table scrolls instead of resizing the workspace.

- [ ] **Step 6: Run tests and commit.**

Run: `dotnet test FourFoldAccountManager.sln --no-restore`

Commit: `git add src/FourFoldAccountManager.Desktop/Views src/FourFoldAccountManager.Desktop/Assets src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs tests; git commit -m "feat: add class comparison plugin"`

### Task 6: Implement the target-level XP Calculator plugin

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Views/ExperienceCalculatorPanel.xaml`
- Create: `src/FourFoldAccountManager.Desktop/Views/ExperienceCalculatorPanel.xaml.cs`
- Create: `src/FourFoldAccountManager.Core/Calculation/ExperienceCalculatorState.cs`
- Create: `tests/FourFoldAccountManager.Core.Tests/ExperienceCalculatorStateTests.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Views/PluginSidebar.xaml.cs`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`

**Interfaces:**
- `ExperienceCalculatorPanel.SetSnapshot(AccountProfile account, PlayerProgressSnapshot snapshot)` selects the active class and initializes target to `current level + 1`.
- `ExperienceCalculatorPanel.ClearSnapshot(string status)` removes stale profile data.
- `ExperienceCalculatorState` owns target-level validation/formatting; target-level changes call `ExperienceCurve.Project(...)` and render `ExperienceProjection` without network access.

- [ ] **Step 1: Add state tests for the target-level interaction.** Assert that a level-25 profile defaults to target 26, level 25 → 33 displays `33,840` at level start, current progress subtracts from that result, same-level target displays zero, lower target displays a validation message, and invalid current XP displays the level-start assumption.

- [ ] **Step 2: Run the state tests and verify they fail.**

Run: `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~ExperienceCalculatorState`

Expected: FAIL because the panel state and formatting contract do not exist.

- [ ] **Step 3: Implement the panel.** Show selected account, active class, current level, current EXP/current cap, target-level numeric input, XP remaining hero result, current/target absolute thresholds, levels remaining, and a compact transition table. Use `BigInteger.ToString("N0", CultureInfo.CurrentCulture)` for display. Keep target input local to the selected account/plugin session and clamp its default only when a newly fetched current level makes the old target invalid.

- [ ] **Step 4: Wire the panel into PluginSidebar.** Selecting XP Calculator uses the already-loaded snapshot when available; otherwise it triggers the same single public-profile refresh path as Class Comparison. Refreshing the profile updates both calculator plugins without changing XP Tracker’s session baseline.

- [ ] **Step 5: Run the full suite/build and commit.**

Run: `dotnet test FourFoldAccountManager.sln --no-restore`

Run: `dotnet build FourFoldAccountManager.sln --no-restore -c Debug`

Commit: `git add src/FourFoldAccountManager.Desktop/Views src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs tests; git commit -m "feat: add target xp calculator plugin"`

### Task 7: Documentation, regression verification, and handoff

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update README capability documentation.** Add Plugins to the capability list; describe that Class Comparison reads the selected account’s active-class base stats from the public profile, compares all nine stats with projected class averages, and keeps equipment as context only. Describe XP Calculator’s target-level formula and the username-to-ranking-ID resolution behavior, including the manual-link exception for users outside the top 200 or ambiguous names.

- [ ] **Step 2: Run formatting/static checks.**

Run: `git diff --check`

Run: `dotnet format FourFoldAccountManager.sln --verify-no-changes --no-restore`

- [ ] **Step 3: Run the complete verification matrix.**

Run: `dotnet test FourFoldAccountManager.sln --no-restore`

Run: `dotnet build FourFoldAccountManager.sln --no-restore -c Debug`

Run: `dotnet build FourFoldAccountManager.sln --no-restore -c Release`

Manual smoke checks in the desktop app:

1. A unique top-200 username loads profile 98-style stats without requiring a manual ID.
2. An out-of-ranking username shows the existing link-profile guidance.
3. Selecting Class Comparison shows the active class’s nine base stats and class-average deltas.
4. Switching accounts clears the previous profile before the new result arrives.
5. XP Calculator level 25 → 33 shows `33,840 XP` from level start and subtracts current EXP when present.
6. XP Tracker rows, resets, overlays, launch/close behavior, and full-screen hiding remain unchanged.

- [ ] **Step 4: Commit documentation and final verification.**

Commit: `git add README.md tests; git commit -m "docs: describe calculator plugins"`

Expected final state: clean worktree, all automated tests passing, Debug and Release builds succeeding, and the implementation branch containing the spec, plan, feature commits, and documentation update.

## Plan self-review

- **Spec coverage:** The plan covers the three-plugin host, complete per-class profile snapshot, username-to-ID resolution, active-class base-stat comparison, XP formula and progress subtraction, palette integration, resource assets, failure states, full-screen behavior, and README documentation.
- **Placeholder scan:** No `TBD`, `TODO`, deferred implementation, or unspecified error-handling steps remain.
- **Type consistency:** `ClassProfileSnapshot` feeds `ClassComparisonCalculator`, `ExperienceCurve`, and the shared `PlayerProfileService`; `PlayerProgressSnapshot` remains the shared profile container; `PluginKind` controls the three host views.
- **Review focus coverage:** Parser edge cases belong to Task 1; identity resolution and cache races to Task 2; formula boundaries to Task 3/6; sidebar lifecycle to Task 4/5/6; selected-account stale-request protection to Task 5.
