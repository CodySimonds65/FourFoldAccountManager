# Task 4 report: estimate text and reset context menu

## Implementation

- Added `TimeUntilNextLevelText` to `XpTrackerRow`, including null handling and positive-duration formatting rounded up to minutes:
  - `<1m time to next level`
  - `Xm time to next level`
  - `Xh Ym time to next level`
  - `Xd Yh time to next level`
- Rendered the estimate below the XP-remaining text with muted, wrapping detail styling.
- Added a two-item row context menu: `Reset XP/hr` and `Reset all`.
- Added `ResetRateRequested` and `ResetAllRequested` panel events. Menu handlers resolve the account ID from the context menu placement target's `XpTrackerRow` data context and ignore other targets.
- Wired both events in `MainWindow` to the coordinator reset operations followed by `RefreshTrackerRows()`.
- Preserved the existing player-link behavior, tracker visibility code, and slot ordering logic.

## TDD coverage

Added `XpTrackerRowAndPanelTests` before implementation. The initial focused test run failed because the requested row property and panel events did not exist. The final focused test run passed 8 tests. Coverage includes estimate boundaries, the unknown estimate, valid placement-target routing for Reset XP/hr, and ignoring invalid placement targets for Reset all.

## Verification

- `dotnet build FourFoldAccountManager.sln -c Release` — passed with 0 warnings and 0 errors.

---

## Review follow-up round 2: rendered Reset all routing

Extended the existing real-DataTemplate test so its actual second-row context menu raises both rendered menu-item click events. It now subscribes to `ResetRateRequested` and `ResetAllRequested`, sets the real menu's placement target to the second rendered row, raises `Reset XP/hr` and `Reset all`, and asserts that both events carry the second row's account ID. No production behavior or fullscreen logic changed.

### Round 2 verification

- Focused rendered-template test — passed: 1 test.
- Desktop tests — passed: 11 tests.
- Core tests — passed: 8 tests.
- `dotnet build FourFoldAccountManager.sln -c Release` — passed with 0 warnings and 0 errors.
- One initial full Desktop-suite attempt timed out in the unrelated coordinator polling test. The test passed in isolation and the complete Desktop-suite rerun passed; no timeout-related change was made.
- `dotnet test tests\\FourFoldAccountManager.Core.Tests\\FourFoldAccountManager.Core.Tests.csproj -c Release` — passed: 8 tests.
- `dotnet test tests\\FourFoldAccountManager.Desktop.Tests\\FourFoldAccountManager.Desktop.Tests.csproj -c Release` — passed: 10 tests.
- `git diff --check` — no whitespace errors.

## Manual/UI check status

The Release WPF app launched. Its existing local data contained one profile, but no active game client or tracker rows, so the required two-row context-menu and reset-state checks could not be performed without creating/launching test accounts and obtaining tracker snapshots. A fullscreen-toggle check was started but Windows automation reported user input before an outcome could be observed, so no manual fullscreen claim is made. Automated panel-event coverage and the Release build validate the implemented behavior; the remaining manual checks need an environment with at least two active tracker rows containing sampled XP data.

## Commit

`feat: add xp tracker reset menu` (includes the implementation, tests, and this report).

---

## Review follow-up: rendered DataTemplate coverage

Added deterministic STA coverage in `XpTrackerRowAndPanelTests.Rendered_row_context_menu_has_reset_items_and_routes_the_clicked_row`.

- The test creates the real WPF `App` and initializes its resource dictionaries, then constructs the real `XpTrackerPanel` rather than an uninitialized control.
- It supplies two rows, hosts the panel in a real `Window`, realizes the generated DataTemplate, finds the second rendered row border, and inspects that row's actual `ContextMenu`.
- It asserts exactly `Reset XP/hr` and `Reset all`, sets the actual template menu's placement target to the second rendered row, raises the real first menu item's click event, and verifies `ResetRateRequested` reports the second row's account ID.
- The initial run was RED because the test harness had not initialized `App.xaml` resources, so panel XAML could not resolve `SidebarCardStyle`. Initializing the application resources was the only support change; no product/fullscreen code changed.

### Follow-up verification

- Focused rendered-template test — passed: 1 test.
- Desktop tests — passed: 11 tests.
- Core tests — passed: 8 tests.
- `dotnet build FourFoldAccountManager.sln -c Release` — passed with 0 warnings and 0 errors.
