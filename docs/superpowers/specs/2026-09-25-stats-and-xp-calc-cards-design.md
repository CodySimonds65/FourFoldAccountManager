# Stats and XP calc overlay cards design

**Status:** Design approved in conversation on 2026-09-25; awaiting written-spec review.

## Context and goal

This is piece 3 of 3 in the plugin overlay work. It builds on:

- Piece 1, `docs/superpowers/specs/2026-09-25-overlay-add-ons-design.md` (PR #19).
- Piece 2, `docs/superpowers/specs/2026-09-25-timer-plugin-design.md` (PR #20).

It is developed on `feature/timer-plugin` and joins PR #20.

**Goal.** In full screen, each client can show its own **Stats** card and **XP calc** card. The cards are switched on per account in the Overlays panel, like the XP/hr card, and stay current without using the sidebar.

This piece also fixes the sidebar **Timer** tab, which lacks the card container that frames the other plugin tabs.

The collapsible Plugins sidebar, whose state is saved across restarts, was built separately as a bounded change in commit `8442435`. It is not part of this spec.

## Current behavior

- **Stats sidebar tab (`ClassComparisonPanel`).** For the selected account's active class, it shows:
  - Above and Below counts versus the class average;
  - the mean percentage delta;
  - a table of the 9 stats (HP, SP, Attack, Magic, Skill, Speed, Luck, Defense, Resistance), each with its profile value, average, and delta.

  The logic is `ClassComparisonDisplayState.FromSnapshot` in Core.
- **XP calc sidebar tab (`ExperienceCalculatorPanel`).** The user types a target level and sees XP remaining, levels remaining, and a per-level breakdown. The logic is `ExperienceCalculatorState` in Core. The target is not saved; switching accounts clears it.
- **Where each tab gets its data.** Both tabs load a profile only for the account selected in the sidebar, on demand.
- **XP tracker.** `XpTrackerCoordinator` polls every open account's public profile about once a minute and keeps the latest successful `PlayerProgressSnapshot` for each account.

## Design

### 1. Data, saved targets, and card content (Core and tracker)

- **Snapshot source.** `XpTrackerCoordinator.GetLatestSnapshot(Guid accountId)` returns the newest successfully fetched snapshot for an account the tracker is active for, or `null` before the first successful fetch. The cards use only this data, so they make no new requests to the website.
- **Saved target levels.** `PanelSettings.XpCalculatorTargetLevels` is an `IReadOnlyDictionary<Guid, long>` that maps an account ID to a target level.
  - `SettingsStore.Validate` drops entries with an empty account ID or a non-positive level.
  - A tolerant JSON converter makes a wrong-shaped value load as an empty map instead of failing the load.
  - Every place that rebuilds `PanelSettings` field by field copies the map: `SettingsStore.Validate`, `PanelLayoutPolicy.WithLayout`, `Assign`, `ClearAccount`, and `CopySettings`.
  - `PanelLayoutPolicy.ClearAccount` removes the account's entry.
- **Sidebar XP calc.**
  - When the account changes, the target box shows that account's saved target, or blank if none is saved.
  - Typing a valid positive target saves it for that account after a 500 ms pause with no further typing.
  - Clearing the box removes the saved target.
  - Invalid text does not change the saved target.
- **Stats card content.** A pure Core helper builds `StatsCardContent` from a snapshot, reusing `ClassComparisonDisplayState`. It contains:
  - the header, the class name and level, for example "Mage 42";
  - the summary, for example "▲8 ▼3 · mean +4.2%";
  - 9 rows, each with the stat label, profile value, average, delta percentage, and direction;
  - a status line used when the data is unavailable.
- **XP calc card content.** A pure Core helper builds `XpCalcCardContent` from a snapshot and an optional saved target, reusing `ExperienceCalculatorState`. It contains:
  - line one, for example "Mage 42 → 50";
  - line two, for example "1,234,567 XP · 8 levels to go";
  - a status line.

  With no saved target, or a target that cannot be used, the target is the next level. A target at or below the current level shows "Target reached".
- **Status texts.**
  - "Waiting for profile…" when no snapshot is available.
  - Otherwise, the existing status from the Core state, for example "The active class is unavailable."

### 2. Cards, the Overlays panel, and the Timer tab (Desktop)

- **Catalog.** Two Account-scoped entries are added:

  | Kind | Display name | Default size | Minimum size |
  |---|---|---|---|
  | `OverlayAddOnKind.Stats = 2` | "Stats" | 260×220 px | 180×150 px |
  | `OverlayAddOnKind.XpCalc = 3` | "XP calc" | 220×56 px | 150×40 px |

  The catalog order becomes XP/hr, Stats, XP calc, Timer. That order sets both the stacking order and the order of switches in the Overlays panel. Persisted kind numbers are unchanged.
- **Card data.**
  - `StatsCardData` and `XpCalcCardData` implement `IOverlayCardData`.
  - Their summaries appear beside the switches in the Overlays panel: for Stats, the arrow counts (for example "▲8 ▼3"); for XP calc, "→ target" (for example "→ 50").
  - `MainWindow.CreateOverlayCardData` gains the account ID, so it can read the tracker snapshot and the saved target.
- **Templates.**
  - **Stats:** account label and header, the summary line, then a 4-column table of stat, profile value, average, and delta. Deltas above average are gold; deltas below average are muted.
  - **XP calc:** "Account · Class Level → Target" over the remaining line.
  - Both templates wrap their content in a `Viewbox` that shrinks the content but never enlarges it, so no content is clipped at the minimum size.
- **Refresh.** The cards rebuild with the XP card whenever `RefreshTrackerRows` runs: after each tracker update, and after a saved target changes. Stale data adds "· stale" to the header, using the tracker's existing stale state.
- **Timer tab.**
  - `TimerPanel` is wrapped in the same `SidebarCardStyle` border, with a padding of 14, used by the XP tracker tab.
  - A heading block is added: "TIMER", small, bold, and gold, over "Speedrun stopwatch", semibold at size 16.
  - The clock, buttons, hotkey line, and lap list sit inside the card.

## Error handling and edge cases

- **No profile yet:** the card shows "Waiting for profile…" until the tracker's first successful fetch.
- **Fetch failed:** the card keeps the last good data and adds "· stale" to the header.
- **Missing or unavailable active class:** the card shows the Core status line.
- **Target reached or invalid:** a target at or below the current level shows "Target reached". An unusable target falls back to the next level.
- **Account removed:** its saved target and its cards are removed.
- **Bad settings data:** wrong types, negative targets, or empty IDs are dropped, and the settings still load.
- **Target cap:** target levels above 9,999 are ignored: they are not saved, they are dropped on load, and the card falls back to the next level.
- **Small cards:** content shrinks to fit; it never clips or scrolls.

## Testing

- **Core:**
  - Stats card content from sample snapshots: header, summary, and rows.
  - XP calc card content: remaining XP and levels to a saved target, fallback to the next level, target reached, missing class, and no snapshot.
  - Target levels: round trip, dropping invalid entries, tolerant loading, preservation at every rebuild site, and removal on `ClearAccount`.
  - The catalog contains Xp, Stats, XpCalc, and Timer with the specified scopes and sizes.
- **Desktop (STA through `WpfTestHost`):**
  - `GetLatestSnapshot` returns the newest snapshot, and `null` before the first fetch.
  - The Stats and XP calc templates render the expected text and fit at their minimum sizes.
  - The sidebar XP calc fills in a saved target when the account changes, and raises one save request after the pause.
  - The Timer tab has the sidebar card container and heading.
- **Manual:**
  1. Switch Stats and XP calc on for two clients in full screen.
  2. Set different targets per account, and confirm each card shows its own.
  3. Confirm the cards refresh after about a minute.
  4. Shrink a Stats card to its minimum size, and confirm nothing is clipped.
  5. Confirm the Timer tab matches the other tabs.

## Out of scope

- Editing targets from the card or the Overlays panel.
- Per-level breakdowns on the XP calc card.
- Additional fetches for accounts that are not open.
- Saving the accounts panel's collapsed state.
