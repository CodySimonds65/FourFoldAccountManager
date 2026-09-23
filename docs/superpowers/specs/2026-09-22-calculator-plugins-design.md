# FourFold Calculator Plugins Design

**Date:** 2026-09-22  
**Status:** Approved conversational design; awaiting written-spec review

## Goal

Add a reusable right-side Plugins surface to FourFold Account Manager with three tools: the existing XP Tracker, a live selected-profile Class Comparison view, and an XP Calculator that projects experience required to a target level.

## User-validated scope

- The existing XP Tracker becomes a plugin inside a reusable sidebar.
- Class Comparison is limited to actual character stats versus the selected class average; damage prediction, equipment, buffs, and DPS ranking are out of scope.
- Class Comparison reads the selected account's live character data from its existing WebView session rather than asking the user to re-enter stats.
- XP Calculator uses the selected account's active class, level, and current XP when available, and lets the user choose a target level.
- All new UI follows the Account Manager's existing dark graphite, warm-gold, green-status, and danger-status palette.
- Stats and calculator inputs are live/in-memory tool state; they are not added to the persisted account profile schema.
- The plugin host must leave room for future tools without another top-level navigation system.

## Recommended architecture

Use a native WPF plugin host in the existing right-side workspace column. The host owns plugin selection and shared selected-account context; each plugin owns its own view and presentation state.

The existing `XpTrackerPanel` remains the source of truth for XP tracker behavior but is rendered through the host. A new `ClassComparisonPanel` renders the nine-stat comparison, and a new `ExperienceCalculatorPanel` renders target-level calculations. `MainWindow` coordinates selected-account changes, plugin selection, and refresh requests but does not perform calculator math or DOM parsing itself.

Do not embed the supplied full HTML calculator. It contains damage prediction and buff-management features that are intentionally out of scope, uses a separate palette, and would require a fragile JavaScript bridge for profile data. Port only the class-average catalog and the `expectedStats` calculation into testable Core code.

## Components and responsibilities

### Plugin host

Create a right-sidebar host with:

- A `PLUGINS` label and tool selector for `XP Tracker`, `Class comparison`, and `XP calculator`.
- A content region that displays exactly one plugin view.
- Existing right-sidebar width, visibility, and full-screen behavior preserved.
- A stable plugin identifier enum or descriptor so future tools can be added without duplicating MainWindow layout logic.

The host should expose the selected account context and a refresh/selection lifecycle to child views through explicit events or methods, not by reaching into `MainWindow` fields.

### Live account snapshot

Add a Core-facing immutable snapshot model with the fields required by both calculator plugins:

```text
AccountGameSnapshot
  AccountId: Guid
  Username: string?
  ActiveClassName: string?
  Level: int?
  CurrentXpInLevel: long?
  NextLevelXp: long?
  BaseStats: IReadOnlyDictionary<CharacterStat, double>
  CapturedAt: DateTimeOffset
```

`CharacterStat` has the fixed order `Hp`, `Sp`, `Attack`, `Magic`, `Skill`, `Speed`, `Luck`, `Defense`, `Resistance`.

Extend `AccountBrowserSessionService` with a read-only operation for an open account session. The operation executes a bounded extraction script in that account's existing WebView and returns a parsed `AccountGameSnapshot` or a typed unavailable result. It must not navigate, submit forms, expose credentials, or send data anywhere.

Keep the browser-facing selector logic isolated in a dedicated extractor/parser. The extraction contract is a JSON object with a version/kind marker, optional identity/progression fields, and the nine numeric stats. The parser normalizes comma-separated numbers, rejects missing or contradictory required fields, and returns a user-visible reason such as `account view is not open`, `game page is still loading`, or `required stat labels were not found`. The UI must never depend directly on DOM selectors.

The first read happens when Class Comparison or XP Calculator becomes active and whenever the selected account changes. A visible `Refresh stats`/`Refresh profile` action performs another read. Cancel an in-flight read when the selected account changes. Do not poll the browser on a timer in this first version.

When a refresh fails after a successful read for the same account, keep the last snapshot visible and show the failure beside its timestamp. When the selected account changes, clear the prior account's snapshot immediately so data cannot appear under the wrong profile.

### Class-average catalog and comparison

Add a Core catalog containing the 16 classes and the exact `base` and `growth` values from `C:\Users\Cody\Desktop\assests\fourfold-calculator-tight.html`:

```text
expectedStats(className, level, stat) = classBase[className, stat]
  + (level - 1) * classGrowth[className, stat]
```

Match the source calculator's whole-number display behavior by rounding projected averages with `Math.Round` before comparison. The comparison engine returns one row per stat with:

- Actual selected-profile value.
- Projected average.
- Actual minus average.
- Percentage difference when the average is nonzero.
- A direction of above, below, or equal.

The summary reports counts above/below average and the mean percentage difference across valid average values. Unknown class names, missing levels, missing stats, or non-finite values produce an unavailable state instead of partial rows.

### Class Comparison plugin UI

Show:

- Account label, active class, level, and last-read timestamp.
- Class portrait when the matching source asset exists; otherwise a two-letter monogram.
- Summary cards for above average, below average, and average difference.
- A scrollable nine-row table in the fixed stat order.
- Refresh action and explicit loading/unavailable states.

Use `Brush.AccentGold` or `Brush.AccentGreen` for positive/healthy values, `Brush.Danger` for below-average values, and the existing muted/neutral brushes for equal or unavailable values. Do not add a second calculator-specific color system.

### XP curve and XP Calculator plugin

Create a Core experience-curve service based on the linked Sacred Seasons formula:

```text
XP needed from level L to L + 1 = 5 * L * (L + 1)
Total XP at the start of level L = (5 / 3) * (L^3 - L)
XP from current level Y to target level W = total(W) - total(Y)
```

Use integer-safe arithmetic (`BigInteger` internally) so higher target levels cannot silently overflow. The calculator validates that levels are positive and that the target is not below the current level.

When current XP-in-level is available and valid, calculate:

```text
currentAbsoluteXp = total(currentLevel) + currentXpInLevel
targetAbsoluteXp = total(targetLevel)
remainingXp = max(0, targetAbsoluteXp - currentAbsoluteXp)
```

When current XP is unavailable, calculate from the start of the current level and label the result `Current progress unavailable; using level start`. Verify a supplied next-level cap against the formula before using current progress. A target equal to the current level returns zero; a lower target is rejected.

For the example level 25 to level 33:

```text
total(25) = 26,000
total(33) = 59,840
remaining from level start = 33,840 XP
```

The plugin UI shows selected account, class, current level, current progress status, target level input, XP remaining, current/target absolute thresholds, levels remaining, and a compact per-level transition breakdown for the selected range.

### Shared selected-profile lifecycle

`MainWindow` passes the selected `AccountProfile.Id` to the plugin host whenever the left account selection changes. The host forwards it to the active plugin. Class Comparison and XP Calculator request a fresh snapshot only for the current selected account. XP Tracker keeps its existing open-slot tracking lifecycle and reset behavior.

If no account is selected, all calculator plugins show an instructional empty state. If the account is selected but its WebView is closed, they show an unavailable state with the account label and explain that the client must be open; they do not launch or navigate the account automatically.

## Data and asset handling

- Copy the 16 class portrait PNGs from `C:\Users\Cody\Desktop\assests\fourfold_assets\classes` into the desktop project's resource assets under a calculator/classes folder.
- Add only the resources needed for native Class Comparison; do not copy the full HTML calculator or its JavaScript into the application.
- Do not change `AccountProfile`, `AccountStore`, or the persisted account JSON schema for this feature.
- Keep the linked XP tracker public-profile data path intact; use its latest progression when available as a fallback for current XP, but live WebView data remains the preferred source for selected-profile stats.

## Failure and safety behavior

- Never read or persist passwords, cookies, or arbitrary page content beyond the bounded snapshot fields.
- A missing WebView session, loading page, unknown class, malformed number, or incomplete stat set becomes a typed unavailable state with actionable UI copy.
- A failed refresh never replaces a valid same-account snapshot with blank data.
- Account switching immediately clears the old account's calculator view.
- No calculator action changes the game client or account profile.

## Testing requirements

Add a Core test project because the current solution has no automated test project. Tests must cover:

- Class catalog lookup and projected averages at level 1 and a higher level.
- All nine comparison rows, rounding, direction, percentages, and summary counts.
- Unknown class, invalid level, missing stat, zero-average, and non-finite input handling.
- Snapshot JSON parsing for valid, loading, incomplete, and contradictory payloads.
- XP formula values for levels 1, 2, 25, 33, and 100.
- Level 25 → 33 equals 33,840 XP from the start of level 25.
- Current-progress subtraction, same-level zero, lower-target rejection, invalid current-progress fallback, and arithmetic overflow safety.
- Selected-account changes clear prior snapshot data and refresh only the active account.
- Plugin selection renders one active view and preserves XP Tracker reset/link behavior.

## Non-goals

- Damage prediction, equipment planning, buff management, DPS ranking, or all-class DPS comparison.
- Manual stat editing or persisted per-account calculator stats.
- Automatic account launching or navigation to make a calculator read succeed.
- Replacing or redesigning the existing XP tracker’s tracking algorithm.
- Network calls from the calculator plugins beyond the existing tracker/profile data path.

## Source reference

- Calculator source: `C:\Users\Cody\Desktop\assests\fourfold-calculator-tight.html`
- Class artwork: `C:\Users\Cody\Desktop\assests\fourfold_assets\classes`
- XP formula: [Sacred Seasons Experience](https://sacredseasons.fandom.com/wiki/Experience)
