# XP per Hour Tracker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show measured XP/hour and active-class XP until next level for every open FourFold account in a right-side rail outside full screen.

**Architecture:** Parse the public EXP ranking to resolve exact usernames to player IDs, then fetch public player profiles for per-class level and current XP. A core calculator turns consecutive snapshots into earned XP, while a desktop coordinator owns polling, storage, client lifecycle, and UI state. The right rail binds to rows keyed by account ID.

**Tech Stack:** .NET 10, C#, WPF, WebView2 app lifecycle, `HttpClient`, AngleSharp 1.8.2, xUnit test project.

**Spec:** `docs/superpowers/specs/2026-09-21-xp-per-hour-tracker-design.md`

## Global Constraints

- Windows desktop app targeting .NET 10; existing WebView2 game sessions and Credential Manager behavior must keep working.
- Public sources: `https://fourfoldonline.com/ranking.php?mode=total_exp&limit=200` and `https://fourfoldonline.com/player.php?id=N` only.
- Poll every two minutes while at least one account view is open; use request timeouts and cancellation.
- No tracker controls or right-rail space in full screen.
- Identity is account `Guid` plus exact public username / verified player ID; never infer identity from the local label.
- Tracker files contain public identifiers and XP snapshots only, never passwords.
- The observed XP cap rule is `5 × level × (level + 1)`; reject intervals whose observed caps disagree.
- Branches use a conventional purpose prefix such as `feature/`, `fix/`, or `docs/`.

## Review Focus

- A player just outside the ranking top 200 can enter a profile URL and be tracked after username verification; test in Task 2.
- Two ranking rows with the same case-insensitive name but different player IDs remain ambiguous; test in Task 2.
- More than one level between samples counts every crossed cap, with overflow-safe math; test in Task 3.
- A changed XP formula or incomplete class card does not fabricate gain; a missing active-class badge leaves XP-to-next-level blank. Test in Tasks 1 and 3.
- A failed poll marks one row stale without resetting another account's tracking session; test in Task 4.

## File map

| File | Responsibility |
| --- | --- |
| `src/FourFoldAccountManager.Core/Tracking/RankingHtmlParser.cs` | Extract public name and positive player ID from EXP table. |
| `src/FourFoldAccountManager.Core/Tracking/PlayerProfileHtmlParser.cs` | Extract profile name and complete per-class progress from class cards. |
| `src/FourFoldAccountManager.Core/Tracking/RankingIdentityResolver.cs` | Exact matching, duplicate detection, manual profile URL validation. |
| `src/FourFoldAccountManager.Core/Tracking/XpProgressCalculator.cs` | Per-class gain and invalid-interval decisions. |
| `src/FourFoldAccountManager.Core/Tracking/XpRateWindow.cs` | Trailing-hour rate and measured session total. |
| `src/FourFoldAccountManager.Core/Tracking/XpTrackingSession.cs` | Per-account baseline, valid intervals, and stale status. |
| `src/FourFoldAccountManager.Core/Data/XpTrackerStore.cs` | Atomic, bounded snapshot persistence. |
| `src/FourFoldAccountManager.Desktop/Services/FourFoldRankingClient.cs` | Cancellable, bounded public-page HTTP fetches. |
| `src/FourFoldAccountManager.Desktop/Services/XpTrackerCoordinator.cs` | Poll scheduling, identity validation, account lifecycle, and row states. |
| `src/FourFoldAccountManager.Desktop/Views/XpTrackerPanel.xaml(.cs)` | Right rail and its binding surface. |
| `src/FourFoldAccountManager.Desktop/MainWindow.xaml(.cs)` | Rail placement, fullscreen behavior, and lifecycle hooks. |
| `src/FourFoldAccountManager.Core/Models/AccountProfile.cs` and account dialog/store | Optional public username and player ID, compatible with existing JSON. |
| `tests/FourFoldAccountManager.Core.Tests/` | Fixture-based parser, identity, arithmetic, rate, and storage tests. |

---

### Task 1: Parse the two public pages

**Files:**
- Modify: `src/FourFoldAccountManager.Core/FourFoldAccountManager.Core.csproj`
- Create: `src/FourFoldAccountManager.Core/Tracking/RankingEntry.cs`
- Create: `src/FourFoldAccountManager.Core/Tracking/ClassXpSnapshot.cs`
- Create: `src/FourFoldAccountManager.Core/Tracking/PlayerProgressSnapshot.cs`
- Create: `src/FourFoldAccountManager.Core/Tracking/RankingHtmlParser.cs`
- Create: `src/FourFoldAccountManager.Core/Tracking/PlayerProfileHtmlParser.cs`
- Create: `tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj`
- Create: `tests/FourFoldAccountManager.Core.Tests/Fixtures/ranking-total-exp.html`
- Create: `tests/FourFoldAccountManager.Core.Tests/Fixtures/player-profile.html`
- Test: `tests/FourFoldAccountManager.Core.Tests/ParsingTests.cs`
- Modify: `FourFoldAccountManager.sln`

**Interfaces:**
- `RankingHtmlParser.Parse(string html) -> IReadOnlyList<RankingEntry>` where `RankingEntry(string Username, int PlayerId)`.
- `PlayerProfileHtmlParser.Parse(string html) -> PlayerProgressSnapshot` where snapshot has `Username`, `string? ActiveClassName`, `IReadOnlyDictionary<string, ClassXpSnapshot> Classes`, and `IReadOnlyList<string> InvalidClasses`; `ClassXpSnapshot(int Level, long CurrentXp, long NextLevelXp, string? SourceUpdated)`.
- Missing page identity or zero class cards throws `InvalidDataException`; an incomplete named card goes into `InvalidClasses` so healthy classes can still be measured.

- [ ] **Step 1: Add the test project and fixtures.** Run `dotnet new xunit -f net10.0 -n FourFoldAccountManager.Core.Tests -o tests/FourFoldAccountManager.Core.Tests`, add a reference to the Core project, and add the test project to `FourFoldAccountManager.sln`. Pin AngleSharp 1.8.2 in the Core project. Save minimal HTML copied from the observed `table tbody tr` and `.class-grid .class-card` shapes; include two classes, comma-formatted numbers, and one malformed card.
- [ ] **Step 2: Write failing parser tests.** For ranking, assert `player.php?id=83` plus `Desmond` yields `RankingEntry("Desmond", 83)` and a link without a positive integer ID fails. For profiles, assert `Level 13`, `EXP 900 / 910`, and the second class become separate keyed snapshots; assert the card with the `Active Class` badge supplies `ActiveClassName` and `NextLevelXp - CurrentXp` is 10. A profile without the badge leaves `ActiveClassName` null; a named card missing EXP is listed in `InvalidClasses` while the healthy class remains. A page without profile identity or any class cards fails. Use `File.ReadAllText` on copied fixture files, with `CopyToOutputDirectory=PreserveNewest`.
- [ ] **Step 3: Run `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~ParsingTests`; confirm the parser tests fail because the types do not exist.**
- [ ] **Step 4: Implement DOM parsing.** Parse with `AngleSharp.Html.Parser.HtmlParser`. Require the EXP ranking's table and read `tbody tr td:nth-child(2) a[href^='player.php?id=']`; parse the `id` query value as a positive integer. Read profile username from the profile hero's `.hero-title`. For each `.class-grid .class-card .class-body`, read `h3`, then find `.meta-item` values by their `strong` label `Level`, `EXP`, and `Updated`; parse numbers with invariant culture after removing group separators. Set `ActiveClassName` only from a card containing the `Active Class` badge, and reject two active badges. Ignore the duplicate highlighted-class summary above the grid. Reject zero cards, missing page username, or duplicate class names as page errors. A named card with missing fields, negative XP, or `CurrentXp >= NextLevelXp` is excluded from `Classes` and listed in `InvalidClasses`.

  ```csharp
  var document = await new HtmlParser().ParseDocumentAsync(html);
  var rows = document.QuerySelectorAll("table tbody tr");
  var cards = document.QuerySelectorAll(".class-grid .class-card .class-body");
  // A card's EXP meta-item has text such as "EXP212,185 / 215,280".
  // Extract by the strong label, then split its remaining text on '/'.
  ```
- [ ] **Step 5: Run the parser tests and commit.** Run `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~ParsingTests`; expect all pass. Commit as `feat: parse public xp profiles`.

### Task 2: Resolve account identity without guessing

**Files:**
- Create: `src/FourFoldAccountManager.Core/Tracking/RankingIdentityResolver.cs`
- Modify: `src/FourFoldAccountManager.Core/Models/AccountProfile.cs`
- Modify: `src/FourFoldAccountManager.Core/Data/AccountStore.cs`
- Test: `tests/FourFoldAccountManager.Core.Tests/IdentityTests.cs`

**Interfaces:**
- Add optional init properties `string? RankingUsername` and `int? RankingPlayerId` to `AccountProfile`; retain its current positional constructor and `Create` method.
- `RankingIdentityResolver.Resolve(string username, IReadOnlyList<RankingEntry> ranking) -> IdentityResolution` with `Matched`, `Missing`, or `Ambiguous` and optional player ID.
- `RankingIdentityResolver.ParsePlayerProfileReference(string input) -> int?` accepts a positive numeric ID or an HTTPS URL on `fourfoldonline.com` with `/player.php?id=N`.

- [ ] **Step 1: Write failing identity tests.** Test ordinal case-insensitive exact match (`"desmond"` to `"Desmond"`), no substring match, duplicate normalized names with different IDs as ambiguous, missing top-200 row, and manual `https://fourfoldonline.com/player.php?id=83`. Reject wrong host, HTTP, zero ID, and unrelated paths. Test that a legacy account JSON row without the new properties loads and retains its GUID and label.
- [ ] **Step 2: Run `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~IdentityTests`; confirm failure for missing behavior.**
- [ ] **Step 3: Implement the resolver and optional profile fields.** Normalize with `Trim()` and `StringComparison.OrdinalIgnoreCase`. In `AccountStore.Validate`, trim a supplied public username, reject whitespace-only values and nonpositive IDs, and preserve nulls for old data. Keep `RankingPlayerId` null until a profile page has been fetched and its public name matches the chosen ranking username. Never use `Label` as a fallback.

  ```csharp
  var matches = ranking.Where(row =>
      string.Equals(row.Username.Trim(), username.Trim(), StringComparison.OrdinalIgnoreCase))
      .Select(row => row.PlayerId).Distinct().ToArray();
  // 0 IDs: Missing; 1 ID: Matched; 2+ IDs: Ambiguous.
  ```
- [ ] **Step 4: Run identity tests and build.** Run the focused test command, then `dotnet build FourFoldAccountManager.sln -c Release`; expect both pass. Commit as `feat: link accounts to ranking players`.

### Task 3: Calculate earned XP and the trailing-hour rate

**Files:**
- Create: `src/FourFoldAccountManager.Core/Tracking/XpProgressCalculator.cs`
- Create: `src/FourFoldAccountManager.Core/Tracking/XpRateWindow.cs`
- Test: `tests/FourFoldAccountManager.Core.Tests/XpProgressCalculatorTests.cs`
- Test: `tests/FourFoldAccountManager.Core.Tests/XpRateWindowTests.cs`

**Interfaces:**
- `XpProgressCalculator.Calculate(PlayerProgressSnapshot before, PlayerProgressSnapshot after) -> XpGainResult(long ValidGain, IReadOnlyList<string> InvalidClasses)`.
- `XpRateWindow.Add(DateTimeOffset from, DateTimeOffset to, long gain)`; `GetRate(DateTimeOffset now) -> double?`; `SessionGain` is a `long`. The first snapshot alone has no rate.

- [ ] **Step 1: Write failing calculator tests.** Cover same-level gain (`300→450` is 150), one level (`level 13, 900/910 → level 14, 100/1050` is 110), and multiple levels (`level 13, 900/910 → level 15, 20/1200` is `10 + 1050 + 20 = 1080`). Include a second unchanged class to show account totals are summed. Test a decreased level, decreased same-level XP, mismatched observed cap, missing class, and values that would overflow `long`; each bad class contributes zero and is listed as invalid while good classes remain valid.
- [ ] **Step 2: Run `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~XpProgressCalculatorTests`; confirm failure.**
- [ ] **Step 3: Implement cap-checked arithmetic.** For each shared class, validate `NextLevelXp == checked(5L * Level * (Level + 1L))` for both snapshots. Same level uses nonnegative difference. Higher level uses `before.NextLevelXp - before.CurrentXp`, adds `5L * level * (level + 1L)` for each fully crossed level, then adds `after.CurrentXp`. Use `checked`; catch overflow as an invalid interval for that class, never silently wrap. If a class is new or missing, rebaseline that class and report it as invalid for the interval.

  ```csharp
  static long Cap(int level) => checked(5L * level * (level + 1L));
  long gain = checked(before.NextLevelXp - before.CurrentXp);
  for (long level = (long)before.Level + 1; level < after.Level; level++)
      gain = checked(gain + Cap(checked((int)level)));
  gain = checked(gain + after.CurrentXp);
  ```
- [ ] **Step 4: Write failing rate-window tests.** A 100 XP gain over two minutes yields 3,000 XP/hour. A second unchanged two-minute interval lowers the rate to 1,500 XP/hour. An interval straddling the one-hour boundary contributes only its overlapping fraction. Verify `SessionGain` remains the sum of measured gains, a single baseline yields null, and invalid intervals are never passed to `Add`.
- [ ] **Step 5: Implement the one-hour window.** Store `(from, to, gain)` intervals, discard intervals entirely before `now - 1 hour`, apportion a boundary interval by overlap/interval duration, then divide included gain by included covered seconds and multiply by 3,600. Reject `to <= from` or negative gain. Do not round internally; format whole XP/hour in the UI.

  ```csharp
  var windowStart = now - TimeSpan.FromHours(1);
  var overlapSeconds = Math.Max(0, (interval.To - (interval.From > windowStart ? interval.From : windowStart)).TotalSeconds);
  var includedGain = interval.Gain * overlapSeconds / (interval.To - interval.From).TotalSeconds;
  // Sum includedGain and covered seconds over intervals; rate = gain / seconds * 3600.
  ```
- [ ] **Step 6: Run both focused suites and commit.** Run `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter 'FullyQualifiedName~XpProgressCalculatorTests|FullyQualifiedName~XpRateWindowTests'`; expect all pass. Commit as `feat: calculate earned xp rates`.

### Task 4: Fetch, persist, and coordinate active sessions

**Files:**
- Modify: `src/FourFoldAccountManager.Core/Data/LocalDataPaths.cs`
- Create: `src/FourFoldAccountManager.Core/Data/XpTrackerStore.cs`
- Create: `src/FourFoldAccountManager.Core/Tracking/XpTrackingSession.cs`
- Create: `src/FourFoldAccountManager.Desktop/Services/FourFoldRankingClient.cs`
- Create: `src/FourFoldAccountManager.Desktop/Services/XpTrackerCoordinator.cs`
- Test: `tests/FourFoldAccountManager.Core.Tests/XpTrackerStoreTests.cs`
- Test: `tests/FourFoldAccountManager.Core.Tests/XpSessionTests.cs`

**Interfaces:**
- `LocalDataPaths.XpTrackerFilePath` points under the existing local app data root.
- `XpTrackerStore.LoadAsync` and `SaveAsync` move a versioned per-account last snapshot plus at most one hour of gain intervals through JSON; account deletion uses `RemoveAsync(Guid)`.
- `FourFoldRankingClient.GetRankingAsync(CancellationToken)` and `GetProfileAsync(int playerId, CancellationToken)` return parsed results through one shared `HttpClient`.
- `XpTrackingSession.ApplySnapshot(PlayerProgressSnapshot snapshot, DateTimeOffset sampledAt)` updates a baseline or adds valid gain intervals; `MarkFetchFailed(DateTimeOffset attemptedAt)` sets stale state without changing the rate; `Stop()` ends the session.
- `XpTrackerCoordinator.Start(Guid accountId, string? rankingUsername, int? playerId)`, `Stop(Guid)`, `RefreshActiveAccounts(...)`, and `DisposeAsync()` manage in-memory sessions and expose row-change notifications.

- [ ] **Step 1: Write failing storage and session tests.** Save and reload two account snapshots, remove one without changing the other, and load malformed tracker JSON as an empty cache without rewriting it. For `XpTrackingSession`, test that the first sample is a baseline, the next valid sample updates rate, `MarkFetchFailed` makes the session stale without changing its rate, and `Stop` ends that session. Exercise two separate instances to prove one failure leaves the other untouched. Pass explicit timestamps; no live network in tests.
- [ ] **Step 2: Run the focused tests and confirm failure.** Run `dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter 'FullyQualifiedName~XpTrackerStoreTests|FullyQualifiedName~XpSessionTests'`.
- [ ] **Step 3: Implement storage.** Add `XpTrackerFilePath = Path.Combine(DataRoot, "xp-tracker.json")`. Save a versioned DTO with account GUID, verified player ID, last profile snapshot, and recent gain intervals using the same temporary-file-plus-`File.Replace` pattern as `AccountStore`; prune data older than one hour. On malformed tracker JSON, return an empty cache and surface a recoverable tracker status; do not alter `accounts.json` or credentials.
- [ ] **Step 4: Implement HTTP fetches.** Set a product User-Agent, a 15-second timeout, and HTTPS base URI. Fetch ranking once per two-minute cycle, then profiles for unique open accounts with at most two concurrent requests. Reject redirects off `fourfoldonline.com`, non-success status, and oversized pages (for example 2 MiB). Use the two parsers from Task 1. Do not navigate account WebViews to scrape data.

  ```csharp
  using var ranking = await httpClient.GetAsync("ranking.php?mode=total_exp&limit=200", cancellationToken);
  ranking.EnsureSuccessStatusCode();
  // For each verified ID, GET only $"player.php?id={playerId}" with the same client.
  ```
- [ ] **Step 5: Implement sessions and coordination.** `XpTrackingSession` starts with no baseline, applies a valid profile through `XpProgressCalculator` and `XpRateWindow`, exposes the latest active-class XP remaining, and preserves its last rate when marked stale. On coordinator `Start`, create a fresh session even if disk has an older snapshot. Resolve an uncached username from the ranking; accept a manual/cached player ID only after fetched profile username verification. Fetch profiles, apply snapshots, persist them, and publish row states on the WPF dispatcher. Catch errors per account and call `MarkFetchFailed` for that account. An unmatched or ambiguous account gets its own actionable status. Cancel polling on `Stop` for the last account and on `DisposeAsync`. A slot move calls `RefreshActiveAccounts` without resetting matching GUID sessions.
- [ ] **Step 6: Run focused tests and commit.** Run the Task 4 test filter and `dotnet build FourFoldAccountManager.sln -c Release`; expect pass. Commit as `feat: collect account xp snapshots`.

### Task 5: Add the right rail and wire every client lifecycle path

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Views/XpTrackerPanel.xaml`
- Create: `src/FourFoldAccountManager.Desktop/Views/XpTrackerPanel.xaml.cs`
- Create: `src/FourFoldAccountManager.Desktop/Views/XpTrackerRow.cs`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Views/AddAccountDialog.xaml(.cs)` if the manual link is entered through the account editor.

**Interfaces:**
- `XpTrackerPanel.ItemsSource` binds to an `ObservableCollection<XpTrackerRow>`; row has `AccountId`, `SlotNumber`, `DisplayName`, `XpPerHourText`, `SessionGainText`, `ActiveClassText`, `XpUntilNextLevelText`, `StatusText`, and `LastUpdatedText`.
- `MainWindow` translates `_openAccountIds` plus `_panelSettings.SlotAccountIds` into sorted row view models and controls rail visibility.

- [ ] **Step 1: Add the rail in XAML.** Extend `MainContentGrid.ColumnDefinitions` with a 16-pixel gap and a roughly 240-pixel right column; put a scrollable `XpTrackerPanel` in that column. Show compact per-account rows with XP/hour, session gain, active class, and `XP to next level`; compute that last value from the latest valid active-class snapshot as `NextLevelXp - CurrentXp`, or show `—` when no active class is marked. Add a `Link player` action for unresolved accounts. Bind text only; do not render raw HTML. Raise `MinWidth` enough to leave the client grid usable, then inspect all six layouts at the minimum width.

  ```xml
  <ColumnDefinition x:Name="TrackerGapColumn" Width="16" />
  <ColumnDefinition x:Name="TrackerColumn" Width="240" />
  <!-- XpTrackerPanel goes in MainContentGrid column 4. -->
  ```
- [ ] **Step 2: Add identity editing.** Add optional `Ranking username` and `Player profile URL or ID` fields to `AddAccountDialog`, retaining the existing saved-login validation. The explicit public username overrides the saved login username; neither comes from the local label. When saving a manual ID, first fetch its public profile and require an exact case-insensitive username match; if it fails or the site is unavailable, keep the dialog open with a clear validation message. When the ranking username changes, clear a cached ID unless this verification succeeds again. Save only a verified positive ID.
- [ ] **Step 3: Wire lifecycle events.** After `_openAccountIds.Add(accountId)` in `StartAssignedAccountAsync`, call coordinator `Start`; after `CloseAccountViewAsync` and `CloseAllOpenViewsAsync`, call `Stop`; after `RebuildPanelAsync`, refresh row order from visible slots without resetting sessions; after account edit/removal, refresh identity or remove stored tracker data. Create the coordinator at window construction, start its loop only when views open, and await disposal in `MainWindow_Closing`.
- [ ] **Step 4: Wire full screen.** In `EnterFullScreen`, collapse the tracker panel and set both right-side column widths to zero alongside the account rail. In `ExitFullScreen`, restore panel visibility and widths according to current open rows. Keep the client grid occupying all available width in full screen. `UpdateTrackerPanelVisibility` should be the single method used after open, close, and fullscreen transitions.
- [ ] **Step 5: Build and inspect Windows UI.** Run `dotnet build FourFoldAccountManager.sln -c Release`. Run the desktop app and verify rows in 1×1, 1×2, 2×1, 2×2, 2×3, and vertical layouts; reorder/replace assigned accounts; close and relaunch a client; enter and leave full screen. Confirm no tracker pixels remain in full screen and a slot move preserves the session number.
- [ ] **Step 6: Commit.** Commit as `feat: show xp tracker beside clients`.

### Task 6: Final verification and user documentation

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Document how tracking works.** Add a short README section explaining the public username/link fields, two-minute polling, the first-sample baseline, rolling XP/hour, active-class XP until next level, level-up handling, top-200 manual-link fallback, stale state, and fullscreen behavior.
- [ ] **Step 2: Run the complete checks.** Run `dotnet test FourFoldAccountManager.sln -c Release` and `dotnet build FourFoldAccountManager.sln -c Release`; inspect exit codes. If a test fails, fix the cause and rerun only the affected suite plus the full test command.
- [ ] **Step 3: Review the final diff against the spec.** Check that no secrets or player-page HTML are persisted, the right rail is absent in full screen, every open account has a row, account removal clears tracker data, and the observed cap rule is guarded. Check `git diff --check`.
- [ ] **Step 4: Commit docs and final adjustments.** Commit as `docs: explain xp tracker`.

## Execution handoff

The tasks depend on shared model and parser interfaces, so implement them in order. Native execution in one session is the simplest approach; request a whole-branch review after Task 6. Do not start implementation until the plan has been reviewed.
