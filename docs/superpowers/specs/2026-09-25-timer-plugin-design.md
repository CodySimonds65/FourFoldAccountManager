# Timer plugin design

**Status:** Design approved in conversation on 2026-09-25; awaiting written-spec review.

## Context and goal

This is piece 2 of 3 in the plugin overlay work. Piece 1 is `docs/superpowers/specs/2026-09-25-overlay-add-ons-design.md` (PR #19). This spec builds on it, and its branch `feature/timer-plugin` is stacked on `feature/overlay-add-ons`.

Speedrunners need a stopwatch they can run without the mouse while playing. The **Timer** plugin adds three things:

- a sidebar tab with the running time and a lap list;
- global split, finish, and reset hotkeys;
- a Global overlay card that can be dragged anywhere in full screen.

**Success:** a user can start, lap, finish, and reset while the game has focus, and afterwards read accurate lap and total times.

## Agreed behavior

- One stopwatch for the whole app, not one per account.
- **Split key:** the first press starts the run and each later press records a lap.
- **Finish key:** stops the run.
- **Reset key and Reset button:** clear everything.
- No pause.
- Times are shown to hundredths of a second.
- The timer keeps running while the app is minimized or unfocused.
- Nothing is saved: closing the app discards the run. There is no personal-best tracking.
- **Overlay card:** shows the large running total and a "Lap N  lap-time" line.

## Design

### 1. Timer engine (Core)

- `SpeedrunTimer` is a UI-free state machine with the states `Ready`, `Running`, and `Finished`.
- **Split:**
  - In Ready, it starts the run.
  - In Running, it records a lap. A lap's time runs from the previous split, or from the start for lap 1.
  - In Finished, it does nothing.
- **Finish:** in Running, it records the final lap and stops. In any other state it does nothing.
- **Reset:** from any state, it returns to Ready with no laps.
- **Time source:** the monotonic high-resolution timestamp from `TimeProvider`, injected for tests. Wall-clock changes do not affect a run. Displayed values are always computed from the start timestamp, never accumulated from UI ticks.
- **Snapshot:** `Snapshot()` returns a `TimerSnapshot` with:
  - `State`;
  - `Total`;
  - `CurrentLapNumber` and `CurrentLapTime` (for a finished run, the last lap);
  - `Laps`, a list of `(Number, LapTime, SplitTotal)`.
- **Formatting:** `TimerFormat` shows `m:ss.ff` under one hour and `h:mm:ss.ff` from one hour, with unbounded hours. Hundredths are truncated, never rounded, so a displayed time is never ahead of the true time.
- **Key repeat:** held keys do not repeat, because the existing hotkeys are registered with no-repeat. There is no other debounce: a quick double press records two laps.

### 2. Hotkeys and settings

- **Single keys.** `GlobalHotkeyChord.IsValid` also accepts these keys with no modifier:
  - F13–F24 (`0x7C`–`0x87`);
  - numpad 0–9 (`0x60`–`0x69`);
  - numpad `*` `+` `-` `.` `/` (`0x6A`, `0x6B`, `0x6D`, `0x6E`, `0x6F`);
  - Pause (`0x13`), Scroll Lock (`0x91`), and Insert (`0x2D`).

  All other keys still need Ctrl, Alt, or Shift, and the existing modifier rules are unchanged. The rule applies to every app shortcut.
- **New `PanelSettings` shortcuts:**

  | Setting | Default |
  |---|---|
  | `TimerSplitShortcut` | Ctrl+Alt+Shift+S (`0x53`) |
  | `TimerFinishShortcut` | Ctrl+Alt+Shift+F (`0x46`) |
  | `TimerResetShortcut` | Ctrl+Alt+Shift+R (`0x52`) |

  Settings files without these properties get the defaults. In `SettingsStore.Validate`, a missing or invalid timer shortcut falls back to its default instead of failing the load. The existing reveal and divider shortcuts keep their current validation. Every place that copies `PanelSettings` field by field copies the three new properties.
- **Shortcut actions.** A single list of shortcut actions (`RevealOverlays`, `ToggleDividerResizing`, `TimerSplit`, `TimerFinish`, `TimerReset`), each mapped to its `PanelSettings` chord, drives:
  - startup registration;
  - `WM_HOTKEY` dispatch;
  - per-action availability;
  - duplicate checks;
  - the Settings dialog rows and the change list.

  It replaces the current per-shortcut flags and hand-written branches. The reveal and divider shortcuts behave exactly as before.
- **Settings dialog.** It adds rows for Timer split, Timer finish, and Timer reset, using the existing key-capture flow. The capture accepts a single key from the allowed set. The note under the rows reads: "Single keys (numpad, F13–F24, Pause, Scroll Lock, Insert) stop reaching games and other apps while FourFold is open."
- **Saving.** The dialog rejects a shortcut already used by another action. Saving uses the existing atomic `TryReplaceAsync` change list: if any registration is refused, none of the changes are applied.
- **Unavailable shortcuts.** A shortcut that duplicates an earlier action (only possible through a hand-edited file) is not registered. A shortcut that Windows refuses is marked unavailable. Both show the existing unavailable status in Settings, and the Timer tab shows a one-line note when any timer shortcut is unavailable.
- **Settings open.** Timer hotkey actions are ignored while the Settings dialog is open.

### 3. Timer tab, overlay card, and live updates (Desktop)

- **`TimerCoordinator`** owns the single `SpeedrunTimer`.
  - `Split()`, `Finish()`, and `Reset()` are called by the hotkeys and the tab buttons.
  - It owns one live `TimerDisplay` (`INotifyPropertyChanged`) with the total text, the lap-line text, the state, the Split button label, and an `ObservableCollection` of laps.
  - A `DispatcherTimer` of about 33 ms refreshes the display only while the timer is Running. There is no ticking in Ready or Finished.
- **Live binding.** The sidebar tab and the overlay card both bind to the same `TimerDisplay`, so ticks update text only. They never call `OverlayCardLayer.SetCards` or `FullscreenOverlayTray.SetRows`, because rebuilding the tray would drop clicks.
- **Sidebar:**
  - `PluginKind.Timer` is appended and `PluginSelectionPolicy` accepts it.
  - The plugin buttons become a 2×2 grid: XP, Stats, XP calc, Timer.
  - The Timer tab shows:
    - the large total and the lap line, in tabular numerals;
    - buttons **Start**/**Split** (the label follows the state), **Finish**, and **Reset**;
    - the lap list (lap number, lap time, split total), oldest first, auto-scrolled to the newest, virtualized;
    - the current hotkey bindings;
    - the unavailable note when relevant.
- **Overlay card:**
  - `OverlayAddOnKind.Timer = 1` is registered in `OverlayAddOnCatalog` as Global scope, display name "Timer", default size 220×60 px, minimum size 150×44 px.
  - `TimerDisplay` implements `IOverlayCardData`. Its `Summary` is empty, because the tray detail does not tick.
  - A `DataTemplate` renders the total and the lap line.
  - `MainWindow.CreateOverlayCardData` returns the coordinator's `TimerDisplay` for `OverlayAddOnKind.Timer`. The Overlays panel then shows its Global section with a Timer switch.
  - The card first appears top-centre and can be dragged anywhere in full screen.
- **Hotkeys work regardless** of whether the tab or the card is visible.

## Error handling and edge cases

- Finish while Ready does nothing. Split after Finish does nothing until Reset. Reset is immediate and has no confirmation.
- Timer hotkeys are ignored while the Settings dialog is open.
- Runs of an hour or more switch to `h:mm:ss.ff`, and hours continue past 24.
- Closing the app mid-run discards the run. Sleep or hibernate during a run is unsupported: the total reflects what the monotonic clock reports.
- If a timer hotkey is unavailable, the tab buttons still work and the tab shows the note.

## Testing

- **Core:**
  - `SpeedrunTimer` with a fake `TimeProvider`: start, laps, finish, ignored actions, reset, and snapshot values.
  - `TimerFormat`: zero, truncation, and the hour format.
  - `GlobalHotkeyChord`: the single-key allow list, rejected single keys, and unchanged modifier rules.
  - `SettingsStore`: timer shortcut defaults for old files, fallback for an invalid timer shortcut, and a round trip. The `PanelLayoutPolicy` copies preserve the timer shortcuts.
  - The catalog test is updated to contain XP (Account) and Timer (Global).
- **Desktop (STA via `WpfTestHost`):**
  - `TimerCoordinator` with a fake clock: display texts, and ticking only while Running.
  - The shortcut-action list: registration, dispatch, duplicate and refused handling, and unchanged behavior for the existing shortcuts (using a fake `IGlobalHotkeyRegistrar`).
  - The Timer tab: the Split button label follows the state, and the unavailable note appears.
  - `FullscreenOverlayTray` shows the Global section with a Timer switch.
- **Manual:**
  1. Bind split to Numpad1, then start, lap, and finish from a focused game.
  2. Switch the Timer card on in full screen and drag it across a client divider.
  3. Try to bind an in-use key and confirm the conflict messages appear.

## Out of scope

- Pause.
- Saved runs, personal bests, and comparisons.
- Named splits.
- Pass-through (non-consuming) keyboard hooks.
- The Stats and XP calc overlay cards (piece 3).
