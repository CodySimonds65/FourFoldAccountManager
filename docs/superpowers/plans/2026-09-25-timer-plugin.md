# Timer Plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a speedrun stopwatch **Timer** plugin with global split, finish, and reset shortcuts; a sidebar tab with the total, the current lap, and a lap list; and a Global full-screen overlay card that can be dragged anywhere.

**Architecture:**
- **Core** owns a UI-free `SpeedrunTimer` state machine on `TimeProvider`, plus `TimerFormat`.
- **Shortcuts:** `GlobalHotkeyChord` accepts an allow-list of single keys. Three timer shortcuts are added to `PanelSettings`. A `GlobalShortcutAction` list maps each action to its settings chord, and a Desktop `GlobalShortcutRegistry` drives startup registration, key-press dispatch, availability, and Settings saves for all five app shortcuts. It replaces the hand-written per-shortcut code.
- **Timer UI:** a Desktop `TimerCoordinator` owns the timer and one live `TimerDisplay` (INotifyPropertyChanged). The sidebar Timer tab and the overlay card both bind to it, so ticks never rebuild the overlay layers or tray.

**Tech Stack:** .NET 10, WPF (net10.0-windows), System.Text.Json, xUnit 2.9, Win32 `RegisterHotKey` (existing).

**Spec:** `docs/superpowers/specs/2026-09-25-timer-plugin-design.md`. It builds on `docs/superpowers/specs/2026-09-25-overlay-add-ons-design.md` (PR #19).

## Global Constraints

- **Branch:** work on `feature/timer-plugin`, which is stacked on `feature/overlay-add-ons` and already has the spec committed. Do not switch branches.
- **Timer behaviour:** one stopwatch for the whole app. States are Ready, Running, and Finished.
  - Split: in Ready it starts the run; in Running it records a lap; in Finished it does nothing.
  - Finish: in Running it records the final lap and stops; otherwise it does nothing.
  - Reset: from any state it returns to Ready with no laps. There is no confirmation.
  - There is no pause, and nothing is saved when the app closes.
- **Time source:** the monotonic `TimeProvider` timestamp, never accumulated UI ticks.
- **Display format:** `m:ss.ff` under one hour and `h:mm:ss.ff` from one hour, with unbounded hours. Hundredths are truncated, never rounded.
- **Lap line:** `Lap {number}  {time}`, with two spaces. In Ready it shows `Lap 1  0:00.00`. In Finished it shows the last lap.
- **Single keys allowed without a modifier:**
  - F13–F24 (`0x7C`–`0x87`)
  - numpad 0–9 (`0x60`–`0x69`)
  - numpad `* + - . /` (`0x6A`, `0x6B`, `0x6D`, `0x6E`, `0x6F`)
  - Pause (`0x13`), Scroll Lock (`0x91`), Insert (`0x2D`)

  All other keys need Ctrl, Alt, or Shift. The existing modifier rules are unchanged, and this rule applies to every app shortcut.
- **Default timer shortcuts:** `TimerSplitShortcut` Ctrl+Alt+Shift+S (`0x53`), `TimerFinishShortcut` Ctrl+Alt+Shift+F (`0x46`), `TimerResetShortcut` Ctrl+Alt+Shift+R (`0x52`).
- **Invalid timer shortcuts:** a missing or invalid timer shortcut in settings falls back to its default and never fails the load. The reveal and divider shortcuts keep their current strict validation.
- **Copying settings:** every place that copies `PanelSettings` field by field must copy the three timer shortcuts. Today these are `SettingsStore.Validate` and `PanelLayoutPolicy.WithLayout`, `Assign`, `ClearAccount`, and `CopySettings`.
- **Shortcut action order:** `RevealOverlays`, `ToggleDividerResizing`, `TimerSplit`, `TimerFinish`, `TimerReset`. When two actions share keys (only possible in a hand-edited file), the earlier action keeps them and the later one stays unregistered.
- **Saving shortcuts:** all shortcut changes are applied through the existing atomic `GlobalHotkeyRegistrationCoordinator.TryReplaceAsync`. If Windows refuses one, nothing is saved.
- **Settings dialog note, verbatim:** "Single keys (numpad, F13–F24, Pause, Scroll Lock, Insert) stop reaching games and other apps while FourFold is open."
- **Settings dialog open:** timer shortcuts do nothing while the Settings dialog is open.
- **Overlay card:** `OverlayAddOnKind.Timer = 1` is registered as Global scope, display name "Timer", default 220×60 px, minimum 150×44 px. Its Overlays-panel summary is empty.
- **Ticking:** only while Running, at about 33 ms. The tab and the card bind to the same live `TimerDisplay`, and ticks never call `OverlayCardLayer.SetCards` or `FullscreenOverlayTray.SetRows`.
- **WPF tests:** every WPF test runs through `WpfTestHost.Run(...)` (`src/FourFoldAccountManager.Desktop.Tests/WpfTestHost.cs`). Never create another `Application`.
- **Commands** (run from the repository root):
  - `dotnet build FourFoldAccountManager.sln -c Release` must show 0 warnings and 0 errors.
  - `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
  - `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`

## Review Focus

- **Settings dialog height.** With three more shortcut rows, the dialog must stay usable on a 1080p or laptop screen. The content scrolls and Save stays reachable. Pinned in Task 4 by a test that the dialog is capped to the work area and scrolls.
- **Shared keys in a hand-edited file.** When the file gives two actions the same keys, changing the later action's keys must leave the earlier action working. Pinned in Task 3 by `ChangingAnActionThatSharedKeysLeavesTheEarlierActionRegistered`.
- **Recording a shortcut.** Recording a shortcut in Settings must not split, finish, or reset a live run. Pinned in Task 5 by `SuspendedShortcutsDoNotChangeTheRun`.
- **Num Lock off.** With Num Lock off, numpad keys arrive as navigation keys and are rejected. The message must tell the user to turn Num Lock on. Pinned in Task 4 by `CapturingANavigationKeyWithoutModifiersIsRejectedWithNumLockHint`.
- **Reset after a long run.** After many laps, Reset must clear the list and the next run must start from lap 1. Pinned in Core by `ResetAfterManyLapsStartsTheNextRunAtLapOne` (Task 1) and in Desktop by `ResetAfterManyLapsClearsTheListAndTheNextRunStartsAtLapOne` (Task 5).

---

## File Structure

**Core (`src/FourFoldAccountManager.Core`)**
- `Timing/SpeedrunTimer.cs` (new): `SpeedrunTimerState`, `TimerLap`, `TimerSnapshot`, and `SpeedrunTimer`.
- `Timing/TimerFormat.cs` (new): time display formatting.
- `Models/GlobalHotkeyChord.cs`: single-key allow-list and timer defaults.
- `Models/GlobalShortcutAction.cs` (new): `GlobalShortcutAction` and `GlobalShortcutActions`, which map each action to its chord and provide display names and duplicate detection.
- `Models/PanelSettings.cs`: three timer shortcut properties.
- `Data/SettingsStore.cs`: validates and falls back the timer shortcuts.
- `Panel/PanelLayoutPolicy.cs`: copies the timer shortcuts.
- `Models/PluginKind.cs` and `Panel/PluginSelectionPolicy.cs`: add `Timer`.
- `Models/OverlayAddOnKind.cs` and `Overlay/OverlayAddOnCatalog.cs`: add `Timer`.

**Desktop (`src/FourFoldAccountManager.Desktop`)**
- `Services/GlobalShortcutRegistry.cs` (new): registration, dispatch, and availability for every shortcut action.
- `Views/ShortcutText.cs` (new): human-readable shortcut text.
- `Views/ShortcutRow.xaml(.cs)` (new): one Settings shortcut row.
- `Views/SettingsDialog.xaml(.cs)`: rows driven by `GlobalShortcutAction`, the Timer section, and scrolling.
- `Views/TimerDisplay.cs` (new): live timer text, the lap rows, and `IOverlayCardData`.
- `Services/TimerCoordinator.cs` (new): owns `SpeedrunTimer`, ticks, and handles timer shortcuts.
- `Views/TimerPanel.xaml(.cs)` (new): the sidebar Timer tab.
- `Views/PluginSidebar.xaml(.cs)`: the Timer button (2×2 grid) and the Timer tab.
- `Resources/OverlayCardTemplates.xaml`: the Timer card template.
- `MainWindow.xaml.cs`: registry wiring, the timer coordinator, and overlay data.
- `README.md`: a Timer paragraph.

**Tests**
- Core: `Timing/ManualTimeProvider.cs`, `Timing/SpeedrunTimerTests.cs`, `Timing/TimerFormatTests.cs`, `Models/GlobalHotkeyChordTests.cs`, `Models/GlobalShortcutActionsTests.cs`, `Data/SettingsStoreTimerShortcutTests.cs`, `Panel/PanelLayoutPolicyTests.cs` (extended), `Panel/PluginSelectionPolicyTests.cs`, and `Overlay/OverlayCardPolicyTests.cs` (catalog test updated).
- Desktop: `ManualTimeProvider.cs`, `Services/GlobalShortcutRegistryTests.cs`, `Views/ShortcutTextTests.cs`, `Views/SettingsDialogTests.cs`, `Services/TimerCoordinatorTests.cs`, `Views/TimerPanelTests.cs`, `Views/PluginSidebarTimerTests.cs`, and `Views/TimerOverlayCardTests.cs`.

---

### Task 1: Speedrun timer engine and time formatting (Core)

**Files:**
- Create: `src/FourFoldAccountManager.Core/Timing/SpeedrunTimer.cs`
- Create: `src/FourFoldAccountManager.Core/Timing/TimerFormat.cs`
- Create: `src/FourFoldAccountManager.Core.Tests/Timing/ManualTimeProvider.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Timing/SpeedrunTimerTests.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Timing/TimerFormatTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (namespace `FourFoldAccountManager.Core.Timing`):
  - `enum SpeedrunTimerState { Ready, Running, Finished }`
  - `record TimerLap(int Number, TimeSpan LapTime, TimeSpan SplitTotal)`
  - `record TimerSnapshot(SpeedrunTimerState State, TimeSpan Total, int CurrentLapNumber, TimeSpan CurrentLapTime, IReadOnlyList<TimerLap> Laps)`
  - `class SpeedrunTimer(TimeProvider? clock = null)` with:
    - `SpeedrunTimerState State`
    - `bool Split()`, which returns true when the press changed the timer
    - `bool Finish()`, which does the same
    - `void Reset()`
    - `TimerSnapshot Snapshot()`
  - `static class TimerFormat` with `string Format(TimeSpan value)`

- [ ] **Step 1: Write the test clock and failing tests**

`src/FourFoldAccountManager.Core.Tests/Timing/ManualTimeProvider.cs`:

```csharp
namespace FourFoldAccountManager.Core.Tests.Timing;

// A monotonic clock the test advances by hand; one timestamp tick is one TimeSpan tick.
internal sealed class ManualTimeProvider : TimeProvider
{
    // A non-zero start proves a timestamp of 0 is not treated specially.
    private long _timestamp = 1_000;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan by) => _timestamp += by.Ticks;
}
```

`src/FourFoldAccountManager.Core.Tests/Timing/SpeedrunTimerTests.cs`:

```csharp
using FourFoldAccountManager.Core.Timing;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Timing;

public sealed class SpeedrunTimerTests
{
    private readonly ManualTimeProvider _clock = new();

    [Fact]
    public void NewTimerIsReadyAtZeroAndIgnoresFinish()
    {
        var timer = new SpeedrunTimer(_clock);

        Assert.False(timer.Finish());
        var snapshot = timer.Snapshot();

        Assert.Equal(SpeedrunTimerState.Ready, snapshot.State);
        Assert.Equal(TimeSpan.Zero, snapshot.Total);
        Assert.Equal(1, snapshot.CurrentLapNumber);
        Assert.Equal(TimeSpan.Zero, snapshot.CurrentLapTime);
        Assert.Empty(snapshot.Laps);
    }

    [Fact]
    public void FirstSplitStartsTheRun()
    {
        var timer = new SpeedrunTimer(_clock);

        Assert.True(timer.Split());
        _clock.Advance(TimeSpan.FromSeconds(1.5));
        var snapshot = timer.Snapshot();

        Assert.Equal(SpeedrunTimerState.Running, snapshot.State);
        Assert.Equal(TimeSpan.FromSeconds(1.5), snapshot.Total);
        Assert.Equal(1, snapshot.CurrentLapNumber);
        Assert.Equal(TimeSpan.FromSeconds(1.5), snapshot.CurrentLapTime);
        Assert.Empty(snapshot.Laps);
    }

    [Fact]
    public void EachLaterSplitRecordsALapFromThePreviousSplit()
    {
        var timer = new SpeedrunTimer(_clock);
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(timer.Split());
        _clock.Advance(TimeSpan.FromSeconds(4));
        Assert.True(timer.Split());
        _clock.Advance(TimeSpan.FromSeconds(3));

        var snapshot = timer.Snapshot();

        Assert.Equal(
        [
            new TimerLap(1, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)),
            new TimerLap(2, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(14))
        ], snapshot.Laps);
        Assert.Equal(TimeSpan.FromSeconds(17), snapshot.Total);
        Assert.Equal(3, snapshot.CurrentLapNumber);
        Assert.Equal(TimeSpan.FromSeconds(3), snapshot.CurrentLapTime);
    }

    [Fact]
    public void FinishRecordsTheFinalLapAndFreezesTheTotal()
    {
        var timer = new SpeedrunTimer(_clock);
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(10));
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(5));

        Assert.True(timer.Finish());
        _clock.Advance(TimeSpan.FromMinutes(1));
        var snapshot = timer.Snapshot();

        Assert.Equal(SpeedrunTimerState.Finished, snapshot.State);
        Assert.Equal(TimeSpan.FromSeconds(15), snapshot.Total);
        Assert.Equal(
        [
            new TimerLap(1, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)),
            new TimerLap(2, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15))
        ], snapshot.Laps);
        Assert.Equal(2, snapshot.CurrentLapNumber);
        Assert.Equal(TimeSpan.FromSeconds(5), snapshot.CurrentLapTime);
    }

    [Fact]
    public void SplitAndFinishDoNothingAfterFinishUntilReset()
    {
        var timer = new SpeedrunTimer(_clock);
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(8));
        timer.Finish();

        Assert.False(timer.Split());
        Assert.False(timer.Finish());
        Assert.Equal(SpeedrunTimerState.Finished, timer.State);

        timer.Reset();
        Assert.Equal(SpeedrunTimerState.Ready, timer.State);
        Assert.True(timer.Split());
        _clock.Advance(TimeSpan.FromSeconds(2));

        var snapshot = timer.Snapshot();
        Assert.Equal(TimeSpan.FromSeconds(2), snapshot.Total);
        Assert.Empty(snapshot.Laps);
    }

    [Fact]
    public void ResetWhileRunningClearsTheRun()
    {
        var timer = new SpeedrunTimer(_clock);
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(3));
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(3));

        timer.Reset();
        var snapshot = timer.Snapshot();

        Assert.Equal(SpeedrunTimerState.Ready, snapshot.State);
        Assert.Equal(TimeSpan.Zero, snapshot.Total);
        Assert.Equal(1, snapshot.CurrentLapNumber);
        Assert.Empty(snapshot.Laps);
    }

    [Fact]
    public void ResetAfterManyLapsStartsTheNextRunAtLapOne()
    {
        var timer = new SpeedrunTimer(_clock);
        timer.Split();
        for (var lap = 0; lap < 200; lap++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            timer.Split();
        }

        Assert.Equal(200, timer.Snapshot().Laps.Count);
        timer.Reset();
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(1));
        timer.Split();

        Assert.Equal([new TimerLap(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))], timer.Snapshot().Laps);
    }
}
```

`src/FourFoldAccountManager.Core.Tests/Timing/TimerFormatTests.cs`:

```csharp
using FourFoldAccountManager.Core.Timing;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Timing;

public sealed class TimerFormatTests
{
    [Theory]
    [InlineData(0L, "0:00.00")]
    [InlineData(52_399_999L, "0:05.23")]
    [InlineData(7_545_600_000L, "12:34.56")]
    [InlineData(35_999_900_000L, "59:59.99")]
    [InlineData(36_000_000_000L, "1:00:00.00")]
    [InlineData(901_840_500_000L, "25:03:04.05")]
    [InlineData(-10_000_000L, "0:00.00")]
    public void FormatsTruncatedHundredthsWithHoursOnlyFromOneHour(long ticks, string expected) =>
        Assert.Equal(expected, TimerFormat.Format(TimeSpan.FromTicks(ticks)));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release --filter "FullyQualifiedName~Timing"`
Expected: the build FAILS with `The type or namespace name 'Timing' does not exist in the namespace 'FourFoldAccountManager.Core'`.

- [ ] **Step 3: Implement the timer**

`src/FourFoldAccountManager.Core/Timing/SpeedrunTimer.cs`:

```csharp
namespace FourFoldAccountManager.Core.Timing;

public enum SpeedrunTimerState
{
    Ready,
    Running,
    Finished
}

public sealed record TimerLap(int Number, TimeSpan LapTime, TimeSpan SplitTotal);

public sealed record TimerSnapshot(
    SpeedrunTimerState State,
    TimeSpan Total,
    int CurrentLapNumber,
    TimeSpan CurrentLapTime,
    IReadOnlyList<TimerLap> Laps);

// A speedrun stopwatch: split starts the run and records laps, finish stops it, reset clears it.
// Times come from the monotonic TimeProvider timestamp, so wall-clock changes never affect a run.
public sealed class SpeedrunTimer
{
    private readonly TimeProvider _clock;
    private readonly List<TimerLap> _laps = [];
    private long _startTimestamp;
    private TimeSpan _finishedTotal;

    public SpeedrunTimer(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public SpeedrunTimerState State { get; private set; } = SpeedrunTimerState.Ready;

    public bool Split()
    {
        switch (State)
        {
            case SpeedrunTimerState.Ready:
                _startTimestamp = _clock.GetTimestamp();
                State = SpeedrunTimerState.Running;
                return true;
            case SpeedrunTimerState.Running:
                RecordLap(Elapsed());
                return true;
            default:
                return false;
        }
    }

    public bool Finish()
    {
        if (State != SpeedrunTimerState.Running)
        {
            return false;
        }

        _finishedTotal = Elapsed();
        RecordLap(_finishedTotal);
        State = SpeedrunTimerState.Finished;
        return true;
    }

    public void Reset()
    {
        _laps.Clear();
        _finishedTotal = TimeSpan.Zero;
        State = SpeedrunTimerState.Ready;
    }

    public TimerSnapshot Snapshot()
    {
        var laps = _laps.ToArray();
        switch (State)
        {
            case SpeedrunTimerState.Running:
            {
                var total = Elapsed();
                var previousSplit = laps.Length > 0 ? laps[^1].SplitTotal : TimeSpan.Zero;
                return new TimerSnapshot(State, total, laps.Length + 1, total - previousSplit, laps);
            }
            case SpeedrunTimerState.Finished:
                return new TimerSnapshot(State, _finishedTotal, laps[^1].Number, laps[^1].LapTime, laps);
            default:
                return new TimerSnapshot(State, TimeSpan.Zero, 1, TimeSpan.Zero, laps);
        }
    }

    private TimeSpan Elapsed() => _clock.GetElapsedTime(_startTimestamp);

    private void RecordLap(TimeSpan splitTotal)
    {
        var previousSplit = _laps.Count > 0 ? _laps[^1].SplitTotal : TimeSpan.Zero;
        _laps.Add(new TimerLap(_laps.Count + 1, splitTotal - previousSplit, splitTotal));
    }
}
```

`src/FourFoldAccountManager.Core/Timing/TimerFormat.cs`:

```csharp
using System.Globalization;

namespace FourFoldAccountManager.Core.Timing;

public static class TimerFormat
{
    private const long TicksPerHundredth = TimeSpan.TicksPerMillisecond * 10;

    // Truncates to hundredths so a displayed time is never ahead of the true time.
    public static string Format(TimeSpan value)
    {
        var hundredths = Math.Max(0, value.Ticks) / TicksPerHundredth;
        var fraction = hundredths % 100;
        var totalSeconds = hundredths / 100;
        var seconds = totalSeconds % 60;
        var minutes = totalSeconds / 60 % 60;
        var hours = totalSeconds / 3600;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}.{fraction:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}.{fraction:00}");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: PASS, including 7 `SpeedrunTimerTests` and 7 `TimerFormatTests` cases.

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/FourFoldAccountManager.Core/Timing src/FourFoldAccountManager.Core.Tests/Timing
git commit -m "feat: add speedrun timer engine and time formatting"
```

---

### Task 2: Single-key shortcuts, timer shortcut settings, and shortcut actions (Core)

**Files:**
- Modify: `src/FourFoldAccountManager.Core/Models/GlobalHotkeyChord.cs`
- Create: `src/FourFoldAccountManager.Core/Models/GlobalShortcutAction.cs`
- Modify: `src/FourFoldAccountManager.Core/Models/PanelSettings.cs`
- Modify: `src/FourFoldAccountManager.Core/Data/SettingsStore.cs` (`Validate`)
- Modify: `src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs` (4 copy sites)
- Test: `src/FourFoldAccountManager.Core.Tests/Models/GlobalHotkeyChordTests.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Models/GlobalShortcutActionsTests.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Data/SettingsStoreTimerShortcutTests.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Panel/PanelLayoutPolicyTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces (namespace `FourFoldAccountManager.Core.Models`):
  - On `GlobalHotkeyChord`:
    - `static GlobalHotkeyChord DefaultTimerSplit`, `DefaultTimerFinish`, and `DefaultTimerReset`
    - `static bool CanBeBoundAlone(ushort virtualKey)`
    - an updated `IsValid`
  - On `PanelSettings`: `GlobalHotkeyChord TimerSplitShortcut`, `TimerFinishShortcut`, and `TimerResetShortcut`, each `{ get; init; }`.
  - `enum GlobalShortcutAction { RevealOverlays, ToggleDividerResizing, TimerSplit, TimerFinish, TimerReset }`
  - `static class GlobalShortcutActions` with:
    - `IReadOnlyList<GlobalShortcutAction> All`
    - `GlobalHotkeyChord GetChord(PanelSettings, GlobalShortcutAction)`
    - `PanelSettings WithChord(PanelSettings, GlobalShortcutAction, GlobalHotkeyChord)`
    - `string DisplayName(GlobalShortcutAction)`
    - `(GlobalShortcutAction First, GlobalShortcutAction Second)? FindDuplicate(IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord>)`

- [ ] **Step 1: Write the failing tests**

`src/FourFoldAccountManager.Core.Tests/Models/GlobalHotkeyChordTests.cs`:

```csharp
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Models;

public sealed class GlobalHotkeyChordTests
{
    private const GlobalHotkeyModifiers All =
        GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt | GlobalHotkeyModifiers.Shift;

    [Theory]
    [InlineData((ushort)0x13)] // Pause
    [InlineData((ushort)0x91)] // Scroll Lock
    [InlineData((ushort)0x2D)] // Insert
    [InlineData((ushort)0x60)] // Numpad 0
    [InlineData((ushort)0x69)] // Numpad 9
    [InlineData((ushort)0x6A)] // Numpad *
    [InlineData((ushort)0x6B)] // Numpad +
    [InlineData((ushort)0x6D)] // Numpad -
    [InlineData((ushort)0x6E)] // Numpad .
    [InlineData((ushort)0x6F)] // Numpad /
    [InlineData((ushort)0x7C)] // F13
    [InlineData((ushort)0x87)] // F24
    public void AllowListedKeysAreValidWithoutModifiers(ushort virtualKey)
    {
        Assert.True(GlobalHotkeyChord.CanBeBoundAlone(virtualKey));
        Assert.True(new GlobalHotkeyChord(virtualKey, GlobalHotkeyModifiers.None).IsValid);
    }

    [Theory]
    [InlineData((ushort)0x41)] // A
    [InlineData((ushort)0x31)] // 1
    [InlineData((ushort)0x70)] // F1
    [InlineData((ushort)0x20)] // Space
    [InlineData((ushort)0x6C)] // Numpad separator
    [InlineData((ushort)0x88)] // just past F24
    [InlineData((ushort)0x23)] // End (numpad 1 with Num Lock off)
    public void OtherKeysStillNeedAModifier(ushort virtualKey)
    {
        Assert.False(GlobalHotkeyChord.CanBeBoundAlone(virtualKey));
        Assert.False(new GlobalHotkeyChord(virtualKey, GlobalHotkeyModifiers.None).IsValid);
        Assert.True(new GlobalHotkeyChord(virtualKey, GlobalHotkeyModifiers.Control).IsValid);
    }

    [Fact]
    public void ExistingModifierRulesAreUnchanged()
    {
        Assert.True(new GlobalHotkeyChord(0x41, All).IsValid);
        Assert.False(new GlobalHotkeyChord(0x5B, GlobalHotkeyModifiers.Control).IsValid); // Windows key
        Assert.False(new GlobalHotkeyChord(0xA0, GlobalHotkeyModifiers.Shift).IsValid); // Left Shift
        Assert.False(new GlobalHotkeyChord(0x41, (GlobalHotkeyModifiers)8).IsValid);
        Assert.False(new GlobalHotkeyChord(0x1F, GlobalHotkeyModifiers.Control).IsValid);
        Assert.True(new GlobalHotkeyChord(0x13, GlobalHotkeyModifiers.Control).IsValid); // Ctrl+Pause
    }

    [Fact]
    public void TimerDefaultsUseAllModifiersWithSFAndR()
    {
        Assert.Equal(new GlobalHotkeyChord(0x53, All), GlobalHotkeyChord.DefaultTimerSplit);
        Assert.Equal(new GlobalHotkeyChord(0x46, All), GlobalHotkeyChord.DefaultTimerFinish);
        Assert.Equal(new GlobalHotkeyChord(0x52, All), GlobalHotkeyChord.DefaultTimerReset);
    }
}
```

`src/FourFoldAccountManager.Core.Tests/Models/GlobalShortcutActionsTests.cs`:

```csharp
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Models;

public sealed class GlobalShortcutActionsTests
{
    private static readonly GlobalHotkeyChord NumPad1 = new(0x61, GlobalHotkeyModifiers.None);

    [Fact]
    public void AllListsActionsInPriorityOrder() =>
        Assert.Equal(
        [
            GlobalShortcutAction.RevealOverlays,
            GlobalShortcutAction.ToggleDividerResizing,
            GlobalShortcutAction.TimerSplit,
            GlobalShortcutAction.TimerFinish,
            GlobalShortcutAction.TimerReset
        ], GlobalShortcutActions.All);

    [Fact]
    public void GetChordReadsEachActionsSetting()
    {
        var settings = PanelSettings.Default;

        Assert.Equal(settings.RevealXpOverlayTabShortcut,
            GlobalShortcutActions.GetChord(settings, GlobalShortcutAction.RevealOverlays));
        Assert.Equal(settings.ToggleDividerResizingShortcut,
            GlobalShortcutActions.GetChord(settings, GlobalShortcutAction.ToggleDividerResizing));
        Assert.Equal(settings.TimerSplitShortcut,
            GlobalShortcutActions.GetChord(settings, GlobalShortcutAction.TimerSplit));
        Assert.Equal(settings.TimerFinishShortcut,
            GlobalShortcutActions.GetChord(settings, GlobalShortcutAction.TimerFinish));
        Assert.Equal(settings.TimerResetShortcut,
            GlobalShortcutActions.GetChord(settings, GlobalShortcutAction.TimerReset));
    }

    [Fact]
    public void WithChordChangesOnlyTheGivenAction()
    {
        foreach (var action in GlobalShortcutActions.All)
        {
            var changed = GlobalShortcutActions.WithChord(PanelSettings.Default, action, NumPad1);

            foreach (var other in GlobalShortcutActions.All)
            {
                Assert.Equal(other == action ? NumPad1 : GlobalShortcutActions.GetChord(PanelSettings.Default, other),
                    GlobalShortcutActions.GetChord(changed, other));
            }
        }
    }

    [Fact]
    public void DefaultShortcutsAreValidAndDistinct()
    {
        var defaults = GlobalShortcutActions.All.ToDictionary(
            action => action, action => GlobalShortcutActions.GetChord(PanelSettings.Default, action));

        Assert.All(defaults.Values, chord => Assert.True(chord.IsValid));
        Assert.Null(GlobalShortcutActions.FindDuplicate(defaults));
    }

    [Fact]
    public void FindDuplicateNamesTheEarlierActionFirst()
    {
        var chords = GlobalShortcutActions.All.ToDictionary(
            action => action, action => GlobalShortcutActions.GetChord(PanelSettings.Default, action));
        chords[GlobalShortcutAction.TimerFinish] = chords[GlobalShortcutAction.RevealOverlays];

        Assert.Equal((GlobalShortcutAction.RevealOverlays, GlobalShortcutAction.TimerFinish),
            GlobalShortcutActions.FindDuplicate(chords));
    }

    [Theory]
    [InlineData(GlobalShortcutAction.RevealOverlays, "Reveal overlays tab")]
    [InlineData(GlobalShortcutAction.ToggleDividerResizing, "Toggle divider resizing")]
    [InlineData(GlobalShortcutAction.TimerSplit, "Timer split")]
    [InlineData(GlobalShortcutAction.TimerFinish, "Timer finish")]
    [InlineData(GlobalShortcutAction.TimerReset, "Timer reset")]
    public void DisplayNamesMatchTheSettingsLabels(GlobalShortcutAction action, string expected) =>
        Assert.Equal(expected, GlobalShortcutActions.DisplayName(action));
}
```

`src/FourFoldAccountManager.Core.Tests/Data/SettingsStoreTimerShortcutTests.cs`:

```csharp
using System.Text.Json.Nodes;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Data;

public sealed class SettingsStoreTimerShortcutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-timer-settings-{Guid.NewGuid():N}");
    private readonly SettingsStore _store;

    public SettingsStoreTimerShortcutTests()
    {
        Directory.CreateDirectory(_root);
        _store = new SettingsStore(new LocalDataPaths(_root));
    }

    private string SettingsPath => Path.Combine(_root, "settings.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task SettingsWithoutTimerShortcutsLoadTheDefaults()
    {
        await WriteSettingsAsync(json =>
        {
            json.Remove("timerSplitShortcut");
            json.Remove("timerFinishShortcut");
            json.Remove("timerResetShortcut");
        });

        var loaded = await _store.LoadAsync();

        Assert.Equal(GlobalHotkeyChord.DefaultTimerSplit, loaded.TimerSplitShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerFinish, loaded.TimerFinishShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerReset, loaded.TimerResetShortcut);
    }

    [Fact]
    public async Task InvalidTimerShortcutsFallBackToDefaultsWithoutFailingTheLoad()
    {
        await WriteSettingsAsync(json =>
        {
            json["timerSplitShortcut"] = new JsonObject { ["virtualKey"] = 0x41, ["modifiers"] = 0 };
            json["timerFinishShortcut"] = null;
            json["timerResetShortcut"] = new JsonObject { ["virtualKey"] = 0x61, ["modifiers"] = 16 };
        });

        var loaded = await _store.LoadAsync();

        Assert.Equal(GlobalHotkeyChord.DefaultTimerSplit, loaded.TimerSplitShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerFinish, loaded.TimerFinishShortcut);
        Assert.Equal(GlobalHotkeyChord.DefaultTimerReset, loaded.TimerResetShortcut);
    }

    [Fact]
    public async Task CustomTimerShortcutsRoundTrip()
    {
        var split = new GlobalHotkeyChord(0x61, GlobalHotkeyModifiers.None);
        var finish = new GlobalHotkeyChord(0x7C, GlobalHotkeyModifiers.None);
        var reset = new GlobalHotkeyChord(0x52, GlobalHotkeyModifiers.Control);

        await _store.SaveAsync(PanelSettings.Default with
        {
            TimerSplitShortcut = split,
            TimerFinishShortcut = finish,
            TimerResetShortcut = reset
        });
        var loaded = await _store.LoadAsync();

        Assert.Equal(split, loaded.TimerSplitShortcut);
        Assert.Equal(finish, loaded.TimerFinishShortcut);
        Assert.Equal(reset, loaded.TimerResetShortcut);
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
    public void SettingsTransformsPreserveTimerShortcuts()
    {
        var accountId = Guid.NewGuid();
        var settings = PanelSettings.Default with
        {
            SlotAccountIds = [accountId, null, null, null, null],
            TimerSplitShortcut = new GlobalHotkeyChord(0x61, GlobalHotkeyModifiers.None),
            TimerFinishShortcut = new GlobalHotkeyChord(0x62, GlobalHotkeyModifiers.None),
            TimerResetShortcut = new GlobalHotkeyChord(0x63, GlobalHotkeyModifiers.None)
        };

        foreach (var transformed in new[]
                 {
                     PanelLayoutPolicy.WithLayout(settings, PanelLayout.OneByTwo),
                     PanelLayoutPolicy.Assign(settings, 1, Guid.NewGuid()),
                     PanelLayoutPolicy.ClearAccount(settings, accountId),
                     PanelLayoutPolicy.WithSplitState(settings, new PanelSplitState("2x2.rows", [0.7, 0.3])),
                     PanelLayoutPolicy.ResetSplitStates(settings)
                 })
        {
            Assert.Equal(settings.TimerSplitShortcut, transformed.TimerSplitShortcut);
            Assert.Equal(settings.TimerFinishShortcut, transformed.TimerFinishShortcut);
            Assert.Equal(settings.TimerResetShortcut, transformed.TimerResetShortcut);
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: the build FAILS with `'GlobalHotkeyChord' does not contain a definition for 'CanBeBoundAlone'` and `'PanelSettings' does not contain a definition for 'TimerSplitShortcut'`.

- [ ] **Step 3: Update `GlobalHotkeyChord`**

In `src/FourFoldAccountManager.Core/Models/GlobalHotkeyChord.cs`, add the timer defaults after `DefaultToggleDividerResizing`:

```csharp
    public static GlobalHotkeyChord DefaultTimerSplit { get; } =
        new(0x53, SupportedModifiers);

    public static GlobalHotkeyChord DefaultTimerFinish { get; } =
        new(0x46, SupportedModifiers);

    public static GlobalHotkeyChord DefaultTimerReset { get; } =
        new(0x52, SupportedModifiers);

    // Keys a speedrunner can bind alone. Every other key needs Ctrl, Alt, or Shift so ordinary typing keeps working.
    private static readonly HashSet<ushort> SingleKeys = BuildSingleKeys();
```

Replace the `IsValid` property with:

```csharp
    [JsonIgnore]
    public bool IsValid =>
        (VirtualKey is >= 0x20 and <= 0xFE || VirtualKey == 0x13) &&
        VirtualKey is not (0x5B or 0x5C or >= 0xA0 and <= 0xA5) &&
        (Modifiers & ~SupportedModifiers) == 0 &&
        (Modifiers != GlobalHotkeyModifiers.None || CanBeBoundAlone(VirtualKey));

    public static bool CanBeBoundAlone(ushort virtualKey) => SingleKeys.Contains(virtualKey);
```

Add this helper at the end of the record:

```csharp
    private static HashSet<ushort> BuildSingleKeys()
    {
        // Pause, Scroll Lock, Insert, and numpad * + - . /
        var keys = new HashSet<ushort> { 0x13, 0x91, 0x2D, 0x6A, 0x6B, 0x6D, 0x6E, 0x6F };
        for (ushort key = 0x60; key <= 0x69; key++)
        {
            keys.Add(key); // Numpad 0-9
        }

        for (ushort key = 0x7C; key <= 0x87; key++)
        {
            keys.Add(key); // F13-F24
        }

        return keys;
    }
```

- [ ] **Step 4: Add the timer shortcuts to `PanelSettings`**

In `src/FourFoldAccountManager.Core/Models/PanelSettings.cs`, add these after the `ToggleDividerResizingShortcut` property:

```csharp
    public GlobalHotkeyChord TimerSplitShortcut { get; init; } = GlobalHotkeyChord.DefaultTimerSplit;

    public GlobalHotkeyChord TimerFinishShortcut { get; init; } = GlobalHotkeyChord.DefaultTimerFinish;

    public GlobalHotkeyChord TimerResetShortcut { get; init; } = GlobalHotkeyChord.DefaultTimerReset;
```

- [ ] **Step 5: Validate and copy the timer shortcuts**

In `src/FourFoldAccountManager.Core/Data/SettingsStore.cs`, in `Validate`'s returned object initializer, add these directly after `ToggleDividerResizingShortcut = settings.ToggleDividerResizingShortcut,`:

```csharp
            TimerSplitShortcut = ValidOrDefault(settings.TimerSplitShortcut, GlobalHotkeyChord.DefaultTimerSplit),
            TimerFinishShortcut = ValidOrDefault(settings.TimerFinishShortcut, GlobalHotkeyChord.DefaultTimerFinish),
            TimerResetShortcut = ValidOrDefault(settings.TimerResetShortcut, GlobalHotkeyChord.DefaultTimerReset),
```

Then add this private method to `SettingsStore`, after `Validate`:

```csharp
    // Timer shortcuts arrived after settings files existed, so a bad one falls back instead of blocking the load.
    private static GlobalHotkeyChord ValidOrDefault(GlobalHotkeyChord? chord, GlobalHotkeyChord fallback) =>
        chord is { IsValid: true } ? chord : fallback;
```

In `src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs`, the line `ToggleDividerResizingShortcut = settings.ToggleDividerResizingShortcut,` appears 4 times (in `WithLayout`, `Assign`, `ClearAccount`, and `CopySettings`). Directly after each occurrence, add:

```csharp
            TimerSplitShortcut = settings.TimerSplitShortcut,
            TimerFinishShortcut = settings.TimerFinishShortcut,
            TimerResetShortcut = settings.TimerResetShortcut,
```

Verify with `grep -c "TimerSplitShortcut = settings.TimerSplitShortcut" src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs`. Expected: `4`.

- [ ] **Step 6: Create the shortcut actions**

`src/FourFoldAccountManager.Core/Models/GlobalShortcutAction.cs`:

```csharp
namespace FourFoldAccountManager.Core.Models;

// Order matters: when a hand-edited settings file gives two actions the same keys, the earlier action keeps them.
public enum GlobalShortcutAction
{
    RevealOverlays,
    ToggleDividerResizing,
    TimerSplit,
    TimerFinish,
    TimerReset
}

public static class GlobalShortcutActions
{
    public static IReadOnlyList<GlobalShortcutAction> All { get; } =
        Array.AsReadOnly(Enum.GetValues<GlobalShortcutAction>());

    public static GlobalHotkeyChord GetChord(PanelSettings settings, GlobalShortcutAction action)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return action switch
        {
            GlobalShortcutAction.RevealOverlays => settings.RevealXpOverlayTabShortcut,
            GlobalShortcutAction.ToggleDividerResizing => settings.ToggleDividerResizingShortcut,
            GlobalShortcutAction.TimerSplit => settings.TimerSplitShortcut,
            GlobalShortcutAction.TimerFinish => settings.TimerFinishShortcut,
            GlobalShortcutAction.TimerReset => settings.TimerResetShortcut,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown shortcut action.")
        };
    }

    public static PanelSettings WithChord(PanelSettings settings, GlobalShortcutAction action, GlobalHotkeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(chord);
        return action switch
        {
            GlobalShortcutAction.RevealOverlays => settings with { RevealXpOverlayTabShortcut = chord },
            GlobalShortcutAction.ToggleDividerResizing => settings with { ToggleDividerResizingShortcut = chord },
            GlobalShortcutAction.TimerSplit => settings with { TimerSplitShortcut = chord },
            GlobalShortcutAction.TimerFinish => settings with { TimerFinishShortcut = chord },
            GlobalShortcutAction.TimerReset => settings with { TimerResetShortcut = chord },
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown shortcut action.")
        };
    }

    public static string DisplayName(GlobalShortcutAction action) => action switch
    {
        GlobalShortcutAction.RevealOverlays => "Reveal overlays tab",
        GlobalShortcutAction.ToggleDividerResizing => "Toggle divider resizing",
        GlobalShortcutAction.TimerSplit => "Timer split",
        GlobalShortcutAction.TimerFinish => "Timer finish",
        GlobalShortcutAction.TimerReset => "Timer reset",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown shortcut action.")
    };

    // Returns the first pair of actions that share keys, earlier action first, or null when all differ.
    public static (GlobalShortcutAction First, GlobalShortcutAction Second)? FindDuplicate(
        IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> chords)
    {
        ArgumentNullException.ThrowIfNull(chords);
        var seen = new Dictionary<GlobalHotkeyChord, GlobalShortcutAction>();
        foreach (var action in All)
        {
            if (!chords.TryGetValue(action, out var chord))
            {
                continue;
            }

            if (seen.TryGetValue(chord, out var earlier))
            {
                return (earlier, action);
            }

            seen.Add(chord, action);
        }

        return null;
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: PASS, including the new `GlobalHotkeyChordTests`, `GlobalShortcutActionsTests`, `SettingsStoreTimerShortcutTests`, and `SettingsTransformsPreserveTimerShortcuts`.

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add src/FourFoldAccountManager.Core src/FourFoldAccountManager.Core.Tests
git commit -m "feat: allow single-key shortcuts and add timer shortcut settings"
```

---

### Task 3: Global shortcut registry and shortcut text (Desktop)

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Services/GlobalShortcutRegistry.cs`
- Create: `src/FourFoldAccountManager.Desktop/Views/ShortcutText.cs`
- Test: `src/FourFoldAccountManager.Desktop.Tests/Services/GlobalShortcutRegistryTests.cs`
- Test: `src/FourFoldAccountManager.Desktop.Tests/Views/ShortcutTextTests.cs`

This task adds new classes only. `MainWindow` switches to them in Task 4.

**Interfaces:**
- Consumes (Task 2): `GlobalShortcutAction`, `GlobalShortcutActions.All/GetChord/WithChord`, the `PanelSettings` timer shortcuts, and `GlobalHotkeyChord`. It also uses the existing internal `IGlobalHotkeyRegistrar` (`TryRegister(int, GlobalHotkeyChord)`, `Unregister(int)`), `GlobalHotkeyRegistrationCoordinator`, and `GlobalHotkeyShortcutChange`, all in `src/FourFoldAccountManager.Desktop/Services/GlobalHotkeyRegistrationCoordinator.cs`.
- Produces:
  - `internal sealed class GlobalShortcutRegistry(IGlobalHotkeyRegistrar registrar) : IDisposable` with:
    - `bool IsAvailable(GlobalShortcutAction)`
    - `void Initialize(PanelSettings)`
    - `bool TryResolve(int hotkeyId, PanelSettings settings, out GlobalShortcutAction action)`
    - `Task<bool> ApplyAsync(PanelSettings current, PanelSettings next, Func<Task> persist)`
  - `internal static class ShortcutText` with `string Format(GlobalHotkeyChord chord)`. Examples: "Ctrl+Alt+Shift+S", "Num 1", "Num *", "F13", "Scroll Lock", "Ctrl+1".

- [ ] **Step 1: Write the failing tests**

`src/FourFoldAccountManager.Desktop.Tests/Services/GlobalShortcutRegistryTests.cs`:

```csharp
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Desktop.Services;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Services;

public sealed class GlobalShortcutRegistryTests
{
    private static readonly GlobalHotkeyChord NumPad1 = new(0x61, GlobalHotkeyModifiers.None);

    [Fact]
    public void InitializeRegistersEveryDefaultShortcutAndResolvesEachToItsAction()
    {
        var registrar = new FakeRegistrar();
        using var registry = new GlobalShortcutRegistry(registrar);
        var settings = PanelSettings.Default;

        registry.Initialize(settings);

        Assert.Equal(5, registrar.Registered.Count);
        foreach (var action in GlobalShortcutActions.All)
        {
            Assert.True(registry.IsAvailable(action));
            Assert.True(registry.TryResolve(
                registrar.IdOf(GlobalShortcutActions.GetChord(settings, action)), settings, out var resolved));
            Assert.Equal(action, resolved);
        }
    }

    [Fact]
    public void LaterActionSharingKeysWithAnEarlierOneStaysUnregistered()
    {
        var registrar = new FakeRegistrar();
        using var registry = new GlobalShortcutRegistry(registrar);
        var settings = PanelSettings.Default with
        {
            TimerSplitShortcut = PanelSettings.Default.RevealXpOverlayTabShortcut
        };

        registry.Initialize(settings);

        Assert.Equal(4, registrar.Registered.Count);
        Assert.True(registry.IsAvailable(GlobalShortcutAction.RevealOverlays));
        Assert.False(registry.IsAvailable(GlobalShortcutAction.TimerSplit));
        Assert.True(registry.TryResolve(
            registrar.IdOf(settings.RevealXpOverlayTabShortcut), settings, out var resolved));
        Assert.Equal(GlobalShortcutAction.RevealOverlays, resolved);
    }

    [Fact]
    public void ShortcutWindowsRefusesIsUnavailable()
    {
        var registrar = new FakeRegistrar();
        registrar.Refused.Add(GlobalHotkeyChord.DefaultTimerReset);
        using var registry = new GlobalShortcutRegistry(registrar);

        registry.Initialize(PanelSettings.Default);

        Assert.False(registry.IsAvailable(GlobalShortcutAction.TimerReset));
        Assert.True(registry.IsAvailable(GlobalShortcutAction.TimerSplit));
    }

    [Fact]
    public void UnknownHotkeyIdDoesNotResolve()
    {
        var registrar = new FakeRegistrar();
        using var registry = new GlobalShortcutRegistry(registrar);
        registry.Initialize(PanelSettings.Default);

        Assert.False(registry.TryResolve(9_999, PanelSettings.Default, out _));
    }

    [Fact]
    public async Task ApplyReplacesAChangedShortcutAfterSaving()
    {
        var registrar = new FakeRegistrar();
        using var registry = new GlobalShortcutRegistry(registrar);
        var current = PanelSettings.Default;
        registry.Initialize(current);
        var next = GlobalShortcutActions.WithChord(current, GlobalShortcutAction.TimerSplit, NumPad1);
        var persisted = 0;

        var applied = await registry.ApplyAsync(current, next, () => { persisted++; return Task.CompletedTask; });

        Assert.True(applied);
        Assert.Equal(1, persisted);
        Assert.DoesNotContain(GlobalHotkeyChord.DefaultTimerSplit, registrar.Registered.Values);
        Assert.True(registry.TryResolve(registrar.IdOf(NumPad1), next, out var resolved));
        Assert.Equal(GlobalShortcutAction.TimerSplit, resolved);
    }

    [Fact]
    public async Task ApplyKeepsTheOldShortcutAndSavesNothingWhenWindowsRefusesTheNewOne()
    {
        var registrar = new FakeRegistrar();
        registrar.Refused.Add(NumPad1);
        using var registry = new GlobalShortcutRegistry(registrar);
        var current = PanelSettings.Default;
        registry.Initialize(current);
        var next = GlobalShortcutActions.WithChord(current, GlobalShortcutAction.TimerSplit, NumPad1);
        var persisted = 0;

        var applied = await registry.ApplyAsync(current, next, () => { persisted++; return Task.CompletedTask; });

        Assert.False(applied);
        Assert.Equal(0, persisted);
        Assert.True(registry.IsAvailable(GlobalShortcutAction.TimerSplit));
        Assert.True(registry.TryResolve(registrar.IdOf(GlobalHotkeyChord.DefaultTimerSplit), current, out var resolved));
        Assert.Equal(GlobalShortcutAction.TimerSplit, resolved);
    }

    [Fact]
    public async Task ChangingAnActionThatSharedKeysLeavesTheEarlierActionRegistered()
    {
        var registrar = new FakeRegistrar();
        using var registry = new GlobalShortcutRegistry(registrar);
        var current = PanelSettings.Default with
        {
            TimerSplitShortcut = PanelSettings.Default.RevealXpOverlayTabShortcut
        };
        registry.Initialize(current);
        var next = GlobalShortcutActions.WithChord(current, GlobalShortcutAction.TimerSplit, NumPad1);

        Assert.True(await registry.ApplyAsync(current, next, () => Task.CompletedTask));

        Assert.True(registry.IsAvailable(GlobalShortcutAction.RevealOverlays));
        Assert.True(registry.IsAvailable(GlobalShortcutAction.TimerSplit));
        Assert.True(registry.TryResolve(registrar.IdOf(next.RevealXpOverlayTabShortcut), next, out var reveal));
        Assert.Equal(GlobalShortcutAction.RevealOverlays, reveal);
        Assert.True(registry.TryResolve(registrar.IdOf(NumPad1), next, out var split));
        Assert.Equal(GlobalShortcutAction.TimerSplit, split);
    }

    [Fact]
    public async Task ApplyWithoutShortcutChangesStillSaves()
    {
        var registrar = new FakeRegistrar();
        using var registry = new GlobalShortcutRegistry(registrar);
        var current = PanelSettings.Default;
        registry.Initialize(current);
        var persisted = 0;

        Assert.True(await registry.ApplyAsync(current, current with { FillGameToPanel = true },
            () => { persisted++; return Task.CompletedTask; }));

        Assert.Equal(1, persisted);
        Assert.Equal(5, registrar.Registered.Count);
    }

    private sealed class FakeRegistrar : IGlobalHotkeyRegistrar
    {
        public HashSet<GlobalHotkeyChord> Refused { get; } = [];

        public Dictionary<int, GlobalHotkeyChord> Registered { get; } = [];

        public bool TryRegister(int id, GlobalHotkeyChord chord)
        {
            if (Refused.Contains(chord) || Registered.ContainsValue(chord))
            {
                return false;
            }

            Registered[id] = chord;
            return true;
        }

        public void Unregister(int id) => Registered.Remove(id);

        public int IdOf(GlobalHotkeyChord chord) => Registered.Single(entry => entry.Value == chord).Key;
    }
}
```

`src/FourFoldAccountManager.Desktop.Tests/Views/ShortcutTextTests.cs`:

```csharp
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class ShortcutTextTests
{
    [Theory]
    [InlineData((ushort)0x53, GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt | GlobalHotkeyModifiers.Shift,
        "Ctrl+Alt+Shift+S")]
    [InlineData((ushort)0x61, GlobalHotkeyModifiers.None, "Num 1")]
    [InlineData((ushort)0x60, GlobalHotkeyModifiers.None, "Num 0")]
    [InlineData((ushort)0x6A, GlobalHotkeyModifiers.None, "Num *")]
    [InlineData((ushort)0x6F, GlobalHotkeyModifiers.None, "Num /")]
    [InlineData((ushort)0x7C, GlobalHotkeyModifiers.None, "F13")]
    [InlineData((ushort)0x91, GlobalHotkeyModifiers.None, "Scroll Lock")]
    [InlineData((ushort)0x13, GlobalHotkeyModifiers.None, "Pause")]
    [InlineData((ushort)0x31, GlobalHotkeyModifiers.Control, "Ctrl+1")]
    public void FormatsShortcutsForPeople(ushort virtualKey, GlobalHotkeyModifiers modifiers, string expected) =>
        Assert.Equal(expected, ShortcutText.Format(new GlobalHotkeyChord(virtualKey, modifiers)));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release --filter "FullyQualifiedName~GlobalShortcutRegistryTests|FullyQualifiedName~ShortcutTextTests"`
Expected: the build FAILS with `The type or namespace name 'GlobalShortcutRegistry' could not be found`.

- [ ] **Step 3: Implement the registry**

`src/FourFoldAccountManager.Desktop/Services/GlobalShortcutRegistry.cs`:

```csharp
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Services;

// Registers every app shortcut action with Windows, maps WM_HOTKEY ids back to actions, and tracks which
// actions are live. The coordinator it wraps identifies registrations by chord, so this class decides which
// action owns a chord.
internal sealed class GlobalShortcutRegistry : IDisposable
{
    private readonly GlobalHotkeyRegistrationCoordinator _coordinator;
    private readonly HashSet<GlobalShortcutAction> _available = [];

    public GlobalShortcutRegistry(IGlobalHotkeyRegistrar registrar)
    {
        _coordinator = new GlobalHotkeyRegistrationCoordinator(registrar);
    }

    public bool IsAvailable(GlobalShortcutAction action) => _available.Contains(action);

    public void Initialize(PanelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _available.Clear();
        RegisterUnavailable(settings);
    }

    public bool TryResolve(int hotkeyId, PanelSettings settings, out GlobalShortcutAction action)
    {
        ArgumentNullException.ThrowIfNull(settings);
        action = default;
        if (!_coordinator.TryGetChord(hotkeyId, out var chord))
        {
            return false;
        }

        foreach (var candidate in GlobalShortcutActions.All)
        {
            if (_available.Contains(candidate) && GlobalShortcutActions.GetChord(settings, candidate) == chord)
            {
                action = candidate;
                return true;
            }
        }

        return false;
    }

    // Replaces live registrations atomically around persist; returns false when Windows refuses a new chord,
    // in which case nothing was persisted and the previous registrations remain.
    public async Task<bool> ApplyAsync(PanelSettings current, PanelSettings next, Func<Task> persist)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(persist);
        var replacements = GlobalShortcutActions.All
            .Where(action => _available.Contains(action) &&
                GlobalShortcutActions.GetChord(current, action) != GlobalShortcutActions.GetChord(next, action))
            .Select(action => new GlobalHotkeyShortcutChange(
                GlobalShortcutActions.GetChord(current, action),
                GlobalShortcutActions.GetChord(next, action)))
            .ToArray();
        if (!await _coordinator.TryReplaceAsync(replacements, persist))
        {
            return false;
        }

        RegisterUnavailable(next);
        return true;
    }

    public void Dispose() => _coordinator.Dispose();

    // Actions without a registration (refused by Windows, or sharing keys with an earlier action) are tried
    // whenever their keys are not already owned by an available action.
    private void RegisterUnavailable(PanelSettings settings)
    {
        foreach (var action in GlobalShortcutActions.All.Where(action => !_available.Contains(action)).ToArray())
        {
            var chord = GlobalShortcutActions.GetChord(settings, action);
            var claimed = GlobalShortcutActions.All.Any(other =>
                _available.Contains(other) && GlobalShortcutActions.GetChord(settings, other) == chord);
            if (!claimed && _coordinator.TryInitialize(chord))
            {
                _available.Add(action);
            }
        }
    }
}
```

- [ ] **Step 4: Implement the shortcut text**

`src/FourFoldAccountManager.Desktop/Views/ShortcutText.cs`:

```csharp
using System.Windows.Input;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

internal static class ShortcutText
{
    public static string Format(GlobalHotkeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        var parts = new List<string>();
        if (chord.Modifiers.HasFlag(GlobalHotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (chord.Modifiers.HasFlag(GlobalHotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (chord.Modifiers.HasFlag(GlobalHotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(KeyName(chord.VirtualKey));
        return string.Join("+", parts);
    }

    private static string KeyName(ushort virtualKey)
    {
        var key = KeyInterop.KeyFromVirtualKey(virtualKey);
        var name = key switch
        {
            Key.Scroll => "Scroll Lock",
            Key.Multiply => "Num *",
            Key.Add => "Num +",
            Key.Subtract => "Num -",
            Key.Decimal => "Num .",
            Key.Divide => "Num /",
            >= Key.NumPad0 and <= Key.NumPad9 => $"Num {key - Key.NumPad0}",
            _ => key.ToString()
        };
        return name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]) ? name[1].ToString() : name;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`
Expected: PASS, including 8 `GlobalShortcutRegistryTests` and 9 `ShortcutTextTests` cases.

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/FourFoldAccountManager.Desktop/Services/GlobalShortcutRegistry.cs src/FourFoldAccountManager.Desktop/Views/ShortcutText.cs src/FourFoldAccountManager.Desktop.Tests
git commit -m "feat: add global shortcut registry and shortcut text"
```

---

### Task 4: Settings rows driven by shortcut actions, and MainWindow on the registry

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Views/ShortcutRow.xaml`
- Create: `src/FourFoldAccountManager.Desktop/Views/ShortcutRow.xaml.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml`
- Replace: `src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml.cs`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`: fields, `MainWindow_SourceInitialized`, `MainWindow_HwndSourceHook`, `MainWindow_Loaded` hotkey initialization, `Settings_Click`, and `CompleteShutdown`
- Test: `src/FourFoldAccountManager.Desktop.Tests/Views/SettingsDialogTests.cs`

**Interfaces:**
- Consumes:
  - Task 2: `GlobalShortcutAction`, `GlobalShortcutActions`, and `GlobalHotkeyChord.TryCreate`.
  - Task 3: `GlobalShortcutRegistry` and `ShortcutText.Format`.
- Produces:
  - `ShortcutRow : UserControl` with:
    - `GlobalShortcutAction Action`
    - `string Title`
    - `string Description`
    - `event EventHandler? CaptureRequested`
    - internal `SetKeysText(string)` and `SetStatus(string)`
    - internal named elements `CaptureButton` and `StatusText`
  - `SettingsDialog`:
    - `internal SettingsDialog(bool fillGameToPanel, bool showFullScreenExitButton, IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> shortcuts, IReadOnlySet<GlobalShortcutAction> unavailableShortcuts, Func<MessageBoxResult>? confirmResetLayoutSizes = null)`
    - `IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> Shortcuts`
    - internal `RowFor(action)`, `BeginCapturingShortcut(action)`, `TryApplyCapturedKey(ushort, GlobalHotkeyModifiers)`, and `DuplicateShortcutMessage()`
    - internal named element `SettingsScrollViewer`
  - `MainWindow`:
    - `private GlobalShortcutRegistry? _shortcuts`
    - `private bool HandleGlobalShortcut(GlobalShortcutAction action)`, whose `default` arm Task 6 extends

- [ ] **Step 1: Write the failing dialog tests**

`src/FourFoldAccountManager.Desktop.Tests/Views/SettingsDialogTests.cs`:

```csharp
using System.Windows;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class SettingsDialogTests
{
    private static readonly GlobalHotkeyChord NumPad1 = new(0x61, GlobalHotkeyModifiers.None);

    [Fact]
    public void RowsShowEachActionsKeysAndAvailability() => WpfTestHost.Run(() =>
    {
        var shortcuts = Defaults();
        shortcuts[GlobalShortcutAction.TimerSplit] = NumPad1;
        var dialog = new SettingsDialog(false, true, shortcuts,
            new HashSet<GlobalShortcutAction> { GlobalShortcutAction.TimerReset });

        Assert.Equal("Num 1", dialog.RowFor(GlobalShortcutAction.TimerSplit).CaptureButton.Content);
        Assert.Equal("Ctrl+Alt+Shift+O", dialog.RowFor(GlobalShortcutAction.RevealOverlays).CaptureButton.Content);
        Assert.StartsWith("Unavailable", dialog.RowFor(GlobalShortcutAction.TimerReset).StatusText.Text);
        Assert.StartsWith("Available globally", dialog.RowFor(GlobalShortcutAction.TimerFinish).StatusText.Text);
        Assert.Equal(shortcuts, dialog.Shortcuts);
    });

    [Fact]
    public void CapturingASingleNumpadKeyUpdatesTheShortcut() => WpfTestHost.Run(() =>
    {
        var dialog = new SettingsDialog(false, true, Defaults(), new HashSet<GlobalShortcutAction>());

        dialog.BeginCapturingShortcut(GlobalShortcutAction.TimerSplit);
        Assert.True(dialog.TryApplyCapturedKey(0x61, GlobalHotkeyModifiers.None));

        Assert.Equal(NumPad1, dialog.Shortcuts[GlobalShortcutAction.TimerSplit]);
        var row = dialog.RowFor(GlobalShortcutAction.TimerSplit);
        Assert.Equal("Num 1", row.CaptureButton.Content);
        Assert.Equal("Save settings to register this shortcut.", row.StatusText.Text);
    });

    [Fact]
    public void CapturingANavigationKeyWithoutModifiersIsRejectedWithNumLockHint() => WpfTestHost.Run(() =>
    {
        var dialog = new SettingsDialog(false, true, Defaults(), new HashSet<GlobalShortcutAction>());

        dialog.BeginCapturingShortcut(GlobalShortcutAction.TimerSplit);
        Assert.False(dialog.TryApplyCapturedKey(0x23, GlobalHotkeyModifiers.None)); // End: numpad 1 with Num Lock off

        Assert.Equal(GlobalHotkeyChord.DefaultTimerSplit, dialog.Shortcuts[GlobalShortcutAction.TimerSplit]);
        var status = dialog.RowFor(GlobalShortcutAction.TimerSplit).StatusText.Text;
        Assert.Contains("Use Ctrl, Alt, or Shift", status);
        Assert.Contains("Num Lock on", status);
    });

    [Fact]
    public void DuplicateShortcutsAreReportedByName() => WpfTestHost.Run(() =>
    {
        var dialog = new SettingsDialog(false, true, Defaults(), new HashSet<GlobalShortcutAction>());
        Assert.Null(dialog.DuplicateShortcutMessage());

        dialog.BeginCapturingShortcut(GlobalShortcutAction.TimerFinish);
        dialog.TryApplyCapturedKey(0x4F,
            GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt | GlobalHotkeyModifiers.Shift);

        Assert.Equal(
            "Reveal overlays tab and Timer finish use the same keys. Choose a different shortcut for one of them.",
            dialog.DuplicateShortcutMessage());
    });

    [Fact]
    public void DialogIsCappedToTheScreenAndScrolls() => WpfTestHost.Run(() =>
    {
        var dialog = new SettingsDialog(false, true, Defaults(), new HashSet<GlobalShortcutAction>());

        Assert.Equal(SystemParameters.WorkArea.Height, dialog.MaxHeight);
        Assert.NotNull(dialog.SettingsScrollViewer);
    });

    private static Dictionary<GlobalShortcutAction, GlobalHotkeyChord> Defaults() =>
        GlobalShortcutActions.All.ToDictionary(
            action => action, action => GlobalShortcutActions.GetChord(PanelSettings.Default, action));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release --filter "FullyQualifiedName~SettingsDialogTests"`
Expected: the build FAILS with `'SettingsDialog' does not contain a constructor that takes 4 arguments` (or similar).

- [ ] **Step 3: Create the shortcut row control**

`src/FourFoldAccountManager.Desktop/Views/ShortcutRow.xaml`:

```xml
<UserControl x:Class="FourFoldAccountManager.Desktop.Views.ShortcutRow"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Border Padding="14" CornerRadius="9" BorderThickness="1" BorderBrush="{DynamicResource Brush.Border}"
            Background="{DynamicResource Brush.Surface}">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <StackPanel VerticalAlignment="Center" Margin="0,0,12,0">
                <TextBlock x:Name="TitleText" FontWeight="SemiBold" />
                <TextBlock x:Name="DescriptionText" Margin="0,4,0,0" Foreground="{DynamicResource Brush.TextMuted}"
                           TextWrapping="Wrap" />
            </StackPanel>
            <Button x:Name="CaptureButton" Grid.Column="1" Width="174" Height="34"
                    Click="CaptureButton_Click" Style="{StaticResource AppButtonStyle}" />
            <TextBlock x:Name="StatusText" Grid.Row="1" Grid.ColumnSpan="2" Margin="0,10,0,0" FontSize="11"
                       Foreground="{DynamicResource Brush.TextMuted}" TextWrapping="Wrap" />
        </Grid>
    </Border>
</UserControl>
```

`src/FourFoldAccountManager.Desktop/Views/ShortcutRow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

public partial class ShortcutRow : UserControl
{
    public ShortcutRow()
    {
        InitializeComponent();
    }

    public event EventHandler? CaptureRequested;

    public GlobalShortcutAction Action { get; set; }

    public string Title
    {
        get => TitleText.Text;
        set
        {
            TitleText.Text = value;
            AutomationProperties.SetName(CaptureButton, $"{value} shortcut");
        }
    }

    public string Description
    {
        get => DescriptionText.Text;
        set => DescriptionText.Text = value;
    }

    internal void SetKeysText(string text) => CaptureButton.Content = text;

    internal void SetStatus(string status) => StatusText.Text = status;

    private void CaptureButton_Click(object sender, RoutedEventArgs e) =>
        CaptureRequested?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 4: Put the dialog rows on `ShortcutRow` and add the Timer section and scrolling**

Make these changes in `src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml`:

1. **Namespace and height cap.** On the `<Window ...>` element, add these attributes:

   ```
   xmlns:views="clr-namespace:FourFoldAccountManager.Desktop.Views"
   MaxHeight="{Binding Source={x:Static SystemParameters.WorkArea}, Path=Height}"
   ```

2. **Scrollable middle row.** In the root `<Grid Margin="26">` `Grid.RowDefinitions`, change the second row from `<RowDefinition Height="Auto" />` to `<RowDefinition Height="*" />`.

3. **Wrap the settings in a scroll viewer.** Replace the opening tag `<StackPanel Grid.Row="1">` with:

   ```xml
   <ScrollViewer x:Name="SettingsScrollViewer" Grid.Row="1" VerticalScrollBarVisibility="Auto"
                 HorizontalScrollBarVisibility="Disabled">
       <StackPanel>
   ```

   Then add `</ScrollViewer>` immediately after that StackPanel's closing `</StackPanel>` (the one just before `<StackPanel Grid.Row="2" ...` with the Cancel and Save buttons).

4. **Reveal row.** Replace the whole `<Border Padding="14" Margin="0,10,0,0" ...>` block that contains `RevealShortcutButton` (from its `<Border` through its matching `</Border>`) with:

   ```xml
   <views:ShortcutRow x:Name="RevealShortcutRow" Action="RevealOverlays" Margin="0,10,0,0"
                      Title="Reveal overlays tab"
                      Description="Show the hidden edge arrow without opening its tray." />
   ```

5. **Divider row.** Replace the whole `<Border ...>` block that contains `ToggleDividerResizeShortcutButton` with:

   ```xml
   <views:ShortcutRow x:Name="DividerShortcutRow" Action="ToggleDividerResizing"
                      Title="Toggle divider resizing"
                      Description="Lock or unlock layout divider dragging with a global shortcut." />
   ```

6. **Timer section.** Immediately after the "Restore divider positions" `</Border>`, before the StackPanel closes, add:

   ```xml
   <TextBlock Text="TIMER" FontSize="10" Foreground="{DynamicResource Brush.AccentGold}" Margin="0,20,0,9" />
   <views:ShortcutRow x:Name="TimerSplitShortcutRow" Action="TimerSplit"
                      Title="Timer split" Description="Start the run, then record a lap on each press." />
   <views:ShortcutRow x:Name="TimerFinishShortcutRow" Action="TimerFinish" Margin="0,10,0,0"
                      Title="Timer finish" Description="Record the final lap and stop the run." />
   <views:ShortcutRow x:Name="TimerResetShortcutRow" Action="TimerReset" Margin="0,10,0,0"
                      Title="Timer reset" Description="Clear the timer and all laps." />
   <TextBlock Margin="2,13,0,0" FontSize="11" Foreground="{DynamicResource Brush.TextMuted}" TextWrapping="Wrap"
              Text="Single keys (numpad, F13–F24, Pause, Scroll Lock, Insert) stop reaching games and other apps while FourFold is open." />
   ```

Verify with: `grep -n "RevealShortcutButton\|ToggleDividerResizeShortcutButton\|RevealShortcutStatusText\|ToggleDividerResizeShortcutStatusText" src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml`
Expected: no output.

- [ ] **Step 5: Replace the dialog code-behind**

Replace all of `src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml.cs` with:

```csharp
using System.Windows;
using System.Windows.Input;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Desktop.Views;

public partial class SettingsDialog : Window
{
    private const string CapturePrompt =
        "Press Ctrl, Alt, or Shift with one key, or a single numpad, F13–F24, Pause, Scroll Lock, or Insert key. Esc cancels.";

    private const string InvalidKeysMessage =
        "Use Ctrl, Alt, or Shift plus one key, or a single numpad (with Num Lock on), F13–F24, Pause, Scroll Lock, or Insert key. Windows-key shortcuts are not supported.";

    private readonly Func<MessageBoxResult>? _confirmResetLayoutSizes;
    private readonly IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> _initialShortcuts;
    private readonly IReadOnlySet<GlobalShortcutAction> _unavailableShortcuts;
    private readonly Dictionary<GlobalShortcutAction, GlobalHotkeyChord> _shortcuts;
    private readonly IReadOnlyDictionary<GlobalShortcutAction, ShortcutRow> _rows;
    private GlobalShortcutAction? _capturingShortcut;

    public SettingsDialog(bool fillGameToPanel, bool showFullScreenExitButton)
        : this(fillGameToPanel, showFullScreenExitButton, DefaultShortcuts(), new HashSet<GlobalShortcutAction>())
    {
    }

    internal SettingsDialog(
        bool fillGameToPanel,
        bool showFullScreenExitButton,
        Func<MessageBoxResult>? confirmResetLayoutSizes)
        : this(fillGameToPanel, showFullScreenExitButton, DefaultShortcuts(), new HashSet<GlobalShortcutAction>(),
            confirmResetLayoutSizes)
    {
    }

    internal SettingsDialog(
        bool fillGameToPanel,
        bool showFullScreenExitButton,
        IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> shortcuts,
        IReadOnlySet<GlobalShortcutAction> unavailableShortcuts,
        Func<MessageBoxResult>? confirmResetLayoutSizes = null)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(unavailableShortcuts);
        if (GlobalShortcutActions.All.Any(action => !shortcuts.ContainsKey(action)))
        {
            throw new ArgumentException("Every shortcut action needs a chord.", nameof(shortcuts));
        }

        _confirmResetLayoutSizes = confirmResetLayoutSizes;
        _initialShortcuts = new Dictionary<GlobalShortcutAction, GlobalHotkeyChord>(shortcuts);
        _shortcuts = new Dictionary<GlobalShortcutAction, GlobalHotkeyChord>(shortcuts);
        _unavailableShortcuts = unavailableShortcuts;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        FillOption.IsChecked = fillGameToPanel;
        FitOption.IsChecked = !fillGameToPanel;
        ShowFullScreenExitOption.IsChecked = showFullScreenExitButton;
        _rows = new[]
        {
            RevealShortcutRow, DividerShortcutRow, TimerSplitShortcutRow, TimerFinishShortcutRow, TimerResetShortcutRow
        }.ToDictionary(row => row.Action);
        foreach (var (action, row) in _rows)
        {
            row.CaptureRequested += (_, _) => BeginCapturingShortcut(action);
            row.SetKeysText(ShortcutText.Format(_shortcuts[action]));
            UpdateShortcutStatus(action);
        }
    }

    public bool FillGameToPanel { get; private set; }

    public bool ShowFullScreenExitButton { get; private set; }

    public IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> Shortcuts => _shortcuts;

    public bool ResetLayoutSizes { get; private set; }

    internal ShortcutRow RowFor(GlobalShortcutAction action) => _rows[action];

    internal void BeginCapturingShortcut(GlobalShortcutAction action)
    {
        _capturingShortcut = action;
        _rows[action].SetKeysText("Press shortcut…");
        _rows[action].SetStatus(CapturePrompt);
        Activate();
        Keyboard.Focus(this);
    }

    // Returns false, and explains why in the row, when the keys cannot be a global shortcut.
    internal bool TryApplyCapturedKey(ushort virtualKey, GlobalHotkeyModifiers modifiers)
    {
        if (_capturingShortcut is not { } action)
        {
            return false;
        }

        if (!GlobalHotkeyChord.TryCreate(virtualKey, modifiers, out var chord))
        {
            _rows[action].SetStatus(InvalidKeysMessage);
            return false;
        }

        _shortcuts[action] = chord;
        _capturingShortcut = null;
        _rows[action].SetKeysText(ShortcutText.Format(chord));
        UpdateShortcutStatus(action);
        return true;
    }

    internal string? DuplicateShortcutMessage()
    {
        if (GlobalShortcutActions.FindDuplicate(_shortcuts) is not { } duplicate)
        {
            return null;
        }

        return $"{GlobalShortcutActions.DisplayName(duplicate.First)} and " +
            $"{GlobalShortcutActions.DisplayName(duplicate.Second)} use the same keys. " +
            "Choose a different shortcut for one of them.";
    }

    private static IReadOnlyDictionary<GlobalShortcutAction, GlobalHotkeyChord> DefaultShortcuts() =>
        GlobalShortcutActions.All.ToDictionary(
            action => action, action => GlobalShortcutActions.GetChord(PanelSettings.Default, action));

    private void ResetLayoutSizes_Click(object sender, RoutedEventArgs e)
    {
        var result = _confirmResetLayoutSizes?.Invoke() ?? MessageBox.Show(
            this,
            "Restore all client layout dividers to their default positions? Game scaling and per-client viewport sizes will not change.",
            "Reset layout sizes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            ResetLayoutSizes = true;
        }
    }

    private void SettingsDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingShortcut is null)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CancelCapture();
            return;
        }

        TryApplyCapturedKey((ushort)KeyInterop.VirtualKeyFromKey(key), MapSupportedModifiers(Keyboard.Modifiers));
    }

    private void CancelCapture()
    {
        if (_capturingShortcut is not { } action)
        {
            return;
        }

        _capturingShortcut = null;
        _rows[action].SetKeysText(ShortcutText.Format(_shortcuts[action]));
        UpdateShortcutStatus(action);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        if (DuplicateShortcutMessage() is { } message)
        {
            MessageBox.Show(this, message, "Shortcuts must be different", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FillGameToPanel = FillOption.IsChecked == true;
        ShowFullScreenExitButton = ShowFullScreenExitOption.IsChecked == true;
        DialogResult = true;
    }

    private void UpdateShortcutStatus(GlobalShortcutAction action)
    {
        _rows[action].SetStatus(_shortcuts[action] != _initialShortcuts[action]
            ? "Save settings to register this shortcut."
            : _unavailableShortcuts.Contains(action)
                ? "Unavailable — another app or another FourFold shortcut may be using these keys. Choose a different combination."
                : "Available globally, including while a game window is focused.");
    }

    private static GlobalHotkeyModifiers MapSupportedModifiers(ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            return GlobalHotkeyModifiers.None;
        }

        var result = GlobalHotkeyModifiers.None;
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            result |= GlobalHotkeyModifiers.Control;
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            result |= GlobalHotkeyModifiers.Alt;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            result |= GlobalHotkeyModifiers.Shift;
        }

        return result;
    }
}
```

The XAML still wires `PreviewKeyDown="SettingsDialog_PreviewKeyDown"`, `Click="ResetLayoutSizes_Click"`, and `Click="Save_Click"`. The old `CaptureRevealShortcut_Click` and `CaptureToggleDividerResizeShortcut_Click` handlers are gone, along with their buttons, which were replaced in Step 4.

- [ ] **Step 6: Move `MainWindow` onto the registry**

Make these changes in `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`, locating each piece by its quoted text.

1. **Fields.** Replace:

```csharp
    private GlobalHotkeyRegistrationCoordinator? _hotkeyCoordinator;
```

with:

```csharp
    private GlobalShortcutRegistry? _shortcuts;
```

and delete these two lines:

```csharp
    private bool _revealShortcutAvailable;
    private bool _toggleDividerResizeShortcutAvailable;
```

2. **`MainWindow_SourceInitialized`.** Replace:

```csharp
        _hotkeyCoordinator = new GlobalHotkeyRegistrationCoordinator(
            new WindowsGlobalHotkeyRegistrar(windowHandle));
```

with:

```csharp
        _shortcuts = new GlobalShortcutRegistry(new WindowsGlobalHotkeyRegistrar(windowHandle));
```

3. **`MainWindow_HwndSourceHook`.** Replace the whole `if (message == WmHotkey && ...) { ... }` block with the following, and add `HandleGlobalShortcut` as a new method directly after `MainWindow_HwndSourceHook`:

```csharp
        if (message == WmHotkey &&
            _shortcuts is { } shortcuts &&
            shortcuts.TryResolve(unchecked((int)wParam.ToInt64()), _panelSettings, out var action) &&
            HandleGlobalShortcut(action))
        {
            handled = true;
        }
```

```csharp
    private bool HandleGlobalShortcut(GlobalShortcutAction action)
    {
        switch (action)
        {
            case GlobalShortcutAction.RevealOverlays:
                FullscreenOverlayTray.RevealEdgeTab();
                return true;
            case GlobalShortcutAction.ToggleDividerResizing:
                ToggleLayoutDividerResizing();
                return true;
            default:
                return false;
        }
    }
```

4. **`MainWindow_Loaded`.** Replace:

```csharp
            _revealShortcutAvailable =
                _hotkeyCoordinator?.TryInitialize(_panelSettings.RevealXpOverlayTabShortcut) == true;
            _toggleDividerResizeShortcutAvailable =
                _panelSettings.ToggleDividerResizingShortcut != _panelSettings.RevealXpOverlayTabShortcut &&
                _hotkeyCoordinator?.TryInitialize(_panelSettings.ToggleDividerResizingShortcut) == true;
```

with:

```csharp
            _shortcuts?.Initialize(_panelSettings);
```

5. **`Settings_Click`.** Replace the entire method with:

```csharp
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(
            _panelSettings.FillGameToPanel,
            _panelSettings.ShowFullScreenExitButton,
            GlobalShortcutActions.All.ToDictionary(
                action => action,
                action => GlobalShortcutActions.GetChord(_panelSettings, action)),
            GlobalShortcutActions.All.Where(action => _shortcuts?.IsAvailable(action) != true).ToHashSet())
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var changedShortcuts = GlobalShortcutActions.All
            .Where(action => dialog.Shortcuts[action] != GlobalShortcutActions.GetChord(_panelSettings, action))
            .ToArray();
        if (dialog.FillGameToPanel == _panelSettings.FillGameToPanel &&
            dialog.ShowFullScreenExitButton == _panelSettings.ShowFullScreenExitButton &&
            changedShortcuts.Length == 0 &&
            !dialog.ResetLayoutSizes)
        {
            return;
        }

        PanelSettings? nextSettings = null;
        var scalingChanged = false;
        SettingsButton.IsEnabled = false;
        try
        {
            PanelSettings WithDialogShortcuts(PanelSettings settings) =>
                GlobalShortcutActions.All.Aggregate(settings,
                    (candidate, action) => GlobalShortcutActions.WithChord(candidate, action, dialog.Shortcuts[action]));

            async Task PersistDialogSettingsAsync()
            {
                nextSettings = await UpdateSettingsAsync(async currentSettings =>
                {
                    var candidate = dialog.ResetLayoutSizes
                        ? PanelLayoutPolicy.ResetSplitStates(currentSettings)
                        : currentSettings;
                    candidate = WithDialogShortcuts(candidate with
                    {
                        FillGameToPanel = dialog.FillGameToPanel,
                        ShowFullScreenExitButton = dialog.ShowFullScreenExitButton
                    });
                    scalingChanged = candidate.FillGameToPanel != currentSettings.FillGameToPanel;
                    if (scalingChanged)
                    {
                        await _browserSessions.SetGameScalingAsync(candidate.FillGameToPanel);
                    }

                    return candidate;
                }, previousSettings => scalingChanged
                    ? _browserSessions.SetGameScalingAsync(previousSettings.FillGameToPanel)
                    : Task.CompletedTask);
            }

            if (changedShortcuts.Length > 0)
            {
                var registered = _shortcuts is not null &&
                    await _shortcuts.ApplyAsync(
                        _panelSettings, WithDialogShortcuts(_panelSettings), PersistDialogSettingsAsync);
                if (!registered)
                {
                    MessageBox.Show(this,
                        "Windows couldn't register one or more new shortcuts. Your previous saved shortcuts and registrations remain unchanged. Choose different combinations and try again.",
                        "Shortcut unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                await PersistDialogSettingsAsync();
            }

            if (nextSettings is null)
            {
                return;
            }

            if (dialog.ResetLayoutSizes)
            {
                await RebuildPanelAsync(closeExistingViews: false);
            }
            if (!nextSettings.FillGameToPanel)
            {
                _viewAdjustmentVisible = false;
                UpdateAllSlotPresentations();
            }
            UpdateManageSlotsButton();
            GlobalStatusText.Text = dialog.ResetLayoutSizes
                ? "Client layout sizes restored to defaults."
                : scalingChanged
                ? nextSettings.FillGameToPanel
                    ? "Game scaling set to Fill panel."
                    : "Game scaling set to Fit entire game."
                : changedShortcuts.Length > 1
                ? "Global shortcuts updated."
                : changedShortcuts.Length == 1
                ? $"{GlobalShortcutActions.DisplayName(changedShortcuts[0])} shortcut updated."
                : nextSettings.ShowFullScreenExitButton
                    ? "Full-screen Exit button enabled."
                    : "Full-screen Exit button hidden. Press Esc to leave full screen.";
        }
        catch
        {
            MessageBox.Show(this, "The settings could not be applied.",
                "FourFold settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SettingsButton.IsEnabled = true;
        }
    }
```

6. **`CompleteShutdown`.** Replace:

```csharp
        var hotkeyCoordinator = _hotkeyCoordinator;
        _hotkeyCoordinator = null;
        try
        {
            hotkeyCoordinator?.Dispose();
        }
```

with:

```csharp
        var shortcuts = _shortcuts;
        _shortcuts = null;
        try
        {
            shortcuts?.Dispose();
        }
```

Verify with: `grep -n "_hotkeyCoordinator\|_revealShortcutAvailable\|_toggleDividerResizeShortcutAvailable\|dialog.RevealXpOverlayTabShortcut\|dialog.ToggleDividerResizingShortcut" src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`
Expected: no output.

- [ ] **Step 7: Run the tests and build to verify they pass**

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`
Expected: PASS, including 5 `SettingsDialogTests`.

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add -A src/FourFoldAccountManager.Desktop src/FourFoldAccountManager.Desktop.Tests
git commit -m "feat: drive shortcut settings and dispatch from shortcut actions"
```

---

### Task 5: Timer coordinator, Timer tab, and sidebar (Desktop)

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Views/TimerDisplay.cs`
- Create: `src/FourFoldAccountManager.Desktop/Services/TimerCoordinator.cs`
- Create: `src/FourFoldAccountManager.Desktop/Views/TimerPanel.xaml`
- Create: `src/FourFoldAccountManager.Desktop/Views/TimerPanel.xaml.cs`
- Modify: `src/FourFoldAccountManager.Core/Models/PluginKind.cs`
- Modify: `src/FourFoldAccountManager.Core/Panel/PluginSelectionPolicy.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Views/PluginSidebar.xaml`
- Modify: `src/FourFoldAccountManager.Desktop/Views/PluginSidebar.xaml.cs`
- Create: `src/FourFoldAccountManager.Desktop.Tests/ManualTimeProvider.cs`
- Test: `src/FourFoldAccountManager.Desktop.Tests/Services/TimerCoordinatorTests.cs`
- Test: `src/FourFoldAccountManager.Desktop.Tests/Views/TimerPanelTests.cs`
- Test: `src/FourFoldAccountManager.Desktop.Tests/Views/PluginSidebarTimerTests.cs`
- Test: `src/FourFoldAccountManager.Core.Tests/Panel/PluginSelectionPolicyTests.cs`

**Interfaces:**
- Consumes:
  - Task 1: `SpeedrunTimer`, `TimerSnapshot`, `SpeedrunTimerState`, and `TimerFormat`.
  - Task 2: `GlobalShortcutAction`.
  - Existing: `IOverlayCardData` (`src/FourFoldAccountManager.Desktop/Views/IOverlayCardData.cs`, a single `string Summary { get; }`) and `WpfTestHost`.
- Produces:
  - `record TimerLapRow(int Number, string LapTimeText, string SplitTotalText)`.
  - `TimerDisplay : INotifyPropertyChanged, IOverlayCardData` with:
    - `string TotalText`, `string LapText`, `SpeedrunTimerState State`, `string SplitButtonText` ("Start" in Ready, otherwise "Split")
    - `ObservableCollection<TimerLapRow> Laps`
    - `string Summary`, which is empty
    - internal `Apply(TimerSnapshot)`
  - `TimerCoordinator : IDisposable` (namespace `FourFoldAccountManager.Desktop.Services`), constructed as `new TimerCoordinator(TimeProvider? clock = null)` on the UI thread, with:
    - `TimerDisplay Display`
    - `Split()`, `Finish()`, and `Reset()`
    - `bool ShortcutsSuspended { get; set; }`
    - `bool TryHandleShortcut(GlobalShortcutAction action)`
    - internal `bool IsTicking` and internal `Refresh()`
  - `TimerPanel : UserControl` with:
    - `Attach(TimerCoordinator)`
    - `SetHotkeys(string splitKeys, string finishKeys, string resetKeys, bool anyUnavailable)`
    - internal named elements `TotalText`, `LapText`, `SplitButton`, `FinishButton`, `ResetButton`, `HotkeyText`, `UnavailableNote`, and `LapsList`
  - `PluginKind.Timer`.
  - `PluginSidebar`:
    - `AttachTimer(TimerCoordinator)`
    - `SetTimerHotkeys(string splitKeys, string finishKeys, string resetKeys, bool anyUnavailable)`
    - internal named elements `TimerButton` and `TimerPanelView`

- [ ] **Step 1: Write the failing tests**

`src/FourFoldAccountManager.Desktop.Tests/ManualTimeProvider.cs`:

```csharp
namespace FourFoldAccountManager.Desktop.Tests;

// A monotonic clock the test advances by hand; one timestamp tick is one TimeSpan tick.
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp = 1_000;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan by) => _timestamp += by.Ticks;
}
```

`src/FourFoldAccountManager.Desktop.Tests/Services/TimerCoordinatorTests.cs`:

```csharp
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Timing;
using FourFoldAccountManager.Desktop.Services;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Services;

public sealed class TimerCoordinatorTests
{
    [Fact]
    public void NewCoordinatorShowsAReadyTimerWithoutTicking() => WpfTestHost.Run(() =>
    {
        using var coordinator = new TimerCoordinator(new ManualTimeProvider());

        Assert.Equal("0:00.00", coordinator.Display.TotalText);
        Assert.Equal("Lap 1  0:00.00", coordinator.Display.LapText);
        Assert.Equal("Start", coordinator.Display.SplitButtonText);
        Assert.Equal(string.Empty, coordinator.Display.Summary);
        Assert.False(coordinator.IsTicking);
    });

    [Fact]
    public void SplitStartsTickingAndRefreshShowsElapsedTime() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);

        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(1.25));
        coordinator.Refresh();

        Assert.True(coordinator.IsTicking);
        Assert.Equal(SpeedrunTimerState.Running, coordinator.Display.State);
        Assert.Equal("0:01.25", coordinator.Display.TotalText);
        Assert.Equal("Lap 1  0:01.25", coordinator.Display.LapText);
        Assert.Equal("Split", coordinator.Display.SplitButtonText);
    });

    [Fact]
    public void LapsAppearAndFinishStopsTicking() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);

        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(10));
        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(5));
        coordinator.Finish();

        Assert.False(coordinator.IsTicking);
        Assert.Equal(
        [
            new TimerLapRow(1, "0:10.00", "0:10.00"),
            new TimerLapRow(2, "0:05.00", "0:15.00")
        ], coordinator.Display.Laps);
        Assert.Equal("0:15.00", coordinator.Display.TotalText);
        Assert.Equal("Lap 2  0:05.00", coordinator.Display.LapText);
    });

    [Fact]
    public void ResetAfterManyLapsClearsTheListAndTheNextRunStartsAtLapOne() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);
        coordinator.Split();
        for (var lap = 0; lap < 200; lap++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            coordinator.Split();
        }

        Assert.Equal(200, coordinator.Display.Laps.Count);
        coordinator.Reset();
        Assert.Empty(coordinator.Display.Laps);
        Assert.Equal("Start", coordinator.Display.SplitButtonText);
        Assert.False(coordinator.IsTicking);

        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(1));
        coordinator.Split();
        Assert.Equal([new TimerLapRow(1, "0:01.00", "0:01.00")], coordinator.Display.Laps);
    });

    [Fact]
    public void TimerShortcutsDriveTheTimerAndOtherActionsAreNotHandled() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);

        Assert.True(coordinator.TryHandleShortcut(GlobalShortcutAction.TimerSplit));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(coordinator.TryHandleShortcut(GlobalShortcutAction.TimerFinish));
        Assert.Equal("0:02.00", coordinator.Display.TotalText);
        Assert.True(coordinator.TryHandleShortcut(GlobalShortcutAction.TimerReset));
        Assert.Equal(SpeedrunTimerState.Ready, coordinator.Display.State);

        Assert.False(coordinator.TryHandleShortcut(GlobalShortcutAction.RevealOverlays));
        Assert.False(coordinator.TryHandleShortcut(GlobalShortcutAction.ToggleDividerResizing));
    });

    [Fact]
    public void SuspendedShortcutsDoNotChangeTheRun() => WpfTestHost.Run(() =>
    {
        using var coordinator = new TimerCoordinator(new ManualTimeProvider());
        coordinator.ShortcutsSuspended = true;

        Assert.True(coordinator.TryHandleShortcut(GlobalShortcutAction.TimerSplit));
        Assert.Equal(SpeedrunTimerState.Ready, coordinator.Display.State);

        coordinator.ShortcutsSuspended = false;
        coordinator.TryHandleShortcut(GlobalShortcutAction.TimerSplit);
        coordinator.ShortcutsSuspended = true;
        coordinator.TryHandleShortcut(GlobalShortcutAction.TimerReset);
        Assert.Equal(SpeedrunTimerState.Running, coordinator.Display.State);
    });

    [Fact]
    public void RefreshRaisesPropertyChangedForTheTotal() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);
        var changed = new List<string?>();
        coordinator.Display.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(1));
        coordinator.Refresh();

        Assert.Contains(nameof(TimerDisplay.TotalText), changed);
        Assert.Contains(nameof(TimerDisplay.SplitButtonText), changed);
    });
}
```

`src/FourFoldAccountManager.Desktop.Tests/Views/TimerPanelTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls.Primitives;
using FourFoldAccountManager.Desktop.Services;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class TimerPanelTests
{
    [Fact]
    public void SplitButtonLabelFollowsTheTimerState() => WpfTestHost.Run(() =>
    {
        using var coordinator = new TimerCoordinator(new ManualTimeProvider());
        var panel = new TimerPanel();
        panel.Attach(coordinator);

        Assert.Equal("Start", panel.SplitButton.Content);
        panel.SplitButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal("Split", panel.SplitButton.Content);
        panel.ResetButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal("Start", panel.SplitButton.Content);
    });

    [Fact]
    public void TotalTextFollowsTheLiveDisplay() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);
        var panel = new TimerPanel();
        panel.Attach(coordinator);

        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(3.5));
        coordinator.Refresh();

        Assert.Equal("0:03.50", panel.TotalText.Text);
        Assert.Equal("Lap 1  0:03.50", panel.LapText.Text);
    });

    [Fact]
    public void HotkeysLineAndUnavailableNote() => WpfTestHost.Run(() =>
    {
        var panel = new TimerPanel();

        panel.SetHotkeys("Num 1", "Num 2", "Num 3", anyUnavailable: false);
        Assert.Equal("Split Num 1 · Finish Num 2 · Reset Num 3", panel.HotkeyText.Text);
        Assert.Equal(Visibility.Collapsed, panel.UnavailableNote.Visibility);

        panel.SetHotkeys("Num 1", "Num 2", "Num 3", anyUnavailable: true);
        Assert.Equal(Visibility.Visible, panel.UnavailableNote.Visibility);
    });
}
```

`src/FourFoldAccountManager.Desktop.Tests/Views/PluginSidebarTimerTests.cs`:

```csharp
using System.Windows;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class PluginSidebarTimerTests
{
    [Fact]
    public void ShowingTheTimerPluginShowsOnlyTheTimerTab() => WpfTestHost.Run(() =>
    {
        var sidebar = new PluginSidebar();

        sidebar.ShowPlugin(PluginKind.Timer);

        Assert.Equal(PluginKind.Timer, sidebar.ActivePlugin);
        Assert.Equal(Visibility.Visible, sidebar.TimerPanelView.Visibility);
        Assert.Equal(Visibility.Collapsed, sidebar.TrackerPanel.Visibility);
        Assert.Equal(Visibility.Collapsed, sidebar.ClassComparisonPanelView.Visibility);
        Assert.Equal(Visibility.Collapsed, sidebar.XpCalculatorPanelView.Visibility);
        Assert.Equal(1d, sidebar.TimerButton.Opacity);
        Assert.Equal(0.65d, sidebar.XpTrackerButton.Opacity);
    });
}
```

`src/FourFoldAccountManager.Core.Tests/Panel/PluginSelectionPolicyTests.cs`:

```csharp
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Panel;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Panel;

public sealed class PluginSelectionPolicyTests
{
    [Theory]
    [InlineData(PluginKind.XpTracker)]
    [InlineData(PluginKind.ClassComparison)]
    [InlineData(PluginKind.XpCalculator)]
    [InlineData(PluginKind.Timer)]
    public void KnownPluginsAreKept(PluginKind plugin) =>
        Assert.Equal(plugin, PluginSelectionPolicy.Normalize(plugin));

    [Fact]
    public void MissingOrUnknownPluginsFallBackToTheTracker()
    {
        Assert.Equal(PluginKind.XpTracker, PluginSelectionPolicy.Normalize(null));
        Assert.Equal(PluginKind.XpTracker, PluginSelectionPolicy.Normalize((PluginKind)99));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release --filter "FullyQualifiedName~Timer"`
Expected: the build FAILS with `The type or namespace name 'TimerCoordinator' could not be found` and `'PluginKind' does not contain a definition for 'Timer'`.

- [ ] **Step 3: Add `PluginKind.Timer`**

In `src/FourFoldAccountManager.Core/Models/PluginKind.cs`, add `Timer` after `XpCalculator` (so it reads `XpCalculator,` then `Timer`). In `src/FourFoldAccountManager.Core/Panel/PluginSelectionPolicy.cs`, replace the method body with:

```csharp
    public static PluginKind Normalize(PluginKind? plugin) => plugin is PluginKind.XpTracker or
        PluginKind.ClassComparison or PluginKind.XpCalculator or PluginKind.Timer ? plugin.Value : PluginKind.XpTracker;
```

- [ ] **Step 4: Create the live display**

`src/FourFoldAccountManager.Desktop/Views/TimerDisplay.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FourFoldAccountManager.Core.Timing;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record TimerLapRow(int Number, string LapTimeText, string SplitTotalText);

// Live timer text shared by the Timer tab and the full-screen Timer card; ticks update it in place,
// so neither the overlay layers nor the Overlays panel are rebuilt while a run is timing.
public sealed class TimerDisplay : INotifyPropertyChanged, IOverlayCardData
{
    private string _totalText = TimerFormat.Format(TimeSpan.Zero);
    private string _lapText = FormatLapLine(1, TimeSpan.Zero);
    private SpeedrunTimerState _state = SpeedrunTimerState.Ready;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TotalText
    {
        get => _totalText;
        private set => Set(ref _totalText, value);
    }

    public string LapText
    {
        get => _lapText;
        private set => Set(ref _lapText, value);
    }

    public SpeedrunTimerState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(SplitButtonText));
            }
        }
    }

    public string SplitButtonText => State == SpeedrunTimerState.Ready ? "Start" : "Split";

    public ObservableCollection<TimerLapRow> Laps { get; } = [];

    // The Overlays panel detail does not tick, so the Timer card shows no summary there.
    public string Summary => string.Empty;

    internal void Apply(TimerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        TotalText = TimerFormat.Format(snapshot.Total);
        LapText = FormatLapLine(snapshot.CurrentLapNumber, snapshot.CurrentLapTime);
        State = snapshot.State;
        if (snapshot.Laps.Count < Laps.Count)
        {
            Laps.Clear();
        }

        for (var index = Laps.Count; index < snapshot.Laps.Count; index++)
        {
            var lap = snapshot.Laps[index];
            Laps.Add(new TimerLapRow(lap.Number, TimerFormat.Format(lap.LapTime), TimerFormat.Format(lap.SplitTotal)));
        }
    }

    private static string FormatLapLine(int number, TimeSpan time) =>
        string.Create(CultureInfo.InvariantCulture, $"Lap {number}  {TimerFormat.Format(time)}");

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

- [ ] **Step 5: Create the coordinator**

`src/FourFoldAccountManager.Desktop/Services/TimerCoordinator.cs`:

```csharp
using System.Windows.Threading;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Timing;
using FourFoldAccountManager.Desktop.Views;

namespace FourFoldAccountManager.Desktop.Services;

// Owns the app's single speedrun timer. Create it on the UI thread; it ticks only while a run is timing.
public sealed class TimerCoordinator : IDisposable
{
    private readonly SpeedrunTimer _timer;
    private readonly DispatcherTimer _ticker;

    public TimerCoordinator(TimeProvider? clock = null)
    {
        _timer = new SpeedrunTimer(clock);
        _ticker = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _ticker.Tick += (_, _) => Refresh();
        Refresh();
    }

    public TimerDisplay Display { get; } = new();

    // Set while the Settings dialog records shortcuts so a key press cannot change the run.
    public bool ShortcutsSuspended { get; set; }

    internal bool IsTicking => _ticker.IsEnabled;

    public void Split()
    {
        if (_timer.Split())
        {
            OnStateChanged();
        }
    }

    public void Finish()
    {
        if (_timer.Finish())
        {
            OnStateChanged();
        }
    }

    public void Reset()
    {
        _timer.Reset();
        OnStateChanged();
    }

    // Returns true for timer actions, whether or not they ran; other actions belong to the caller.
    public bool TryHandleShortcut(GlobalShortcutAction action)
    {
        switch (action)
        {
            case GlobalShortcutAction.TimerSplit:
                if (!ShortcutsSuspended)
                {
                    Split();
                }

                return true;
            case GlobalShortcutAction.TimerFinish:
                if (!ShortcutsSuspended)
                {
                    Finish();
                }

                return true;
            case GlobalShortcutAction.TimerReset:
                if (!ShortcutsSuspended)
                {
                    Reset();
                }

                return true;
            default:
                return false;
        }
    }

    // Display values are recomputed from the timer on each tick, never accumulated.
    internal void Refresh() => Display.Apply(_timer.Snapshot());

    public void Dispose() => _ticker.Stop();

    private void OnStateChanged()
    {
        Refresh();
        _ticker.IsEnabled = _timer.State == SpeedrunTimerState.Running;
    }
}
```

- [ ] **Step 6: Create the Timer tab**

`src/FourFoldAccountManager.Desktop/Views/TimerPanel.xaml`:

```xml
<UserControl x:Class="FourFoldAccountManager.Desktop.Views.TimerPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <TextBlock x:Name="TotalText" Text="{Binding TotalText}" FontSize="32" FontWeight="SemiBold"
                   Typography.NumeralAlignment="Tabular" Foreground="{DynamicResource Brush.AccentGold}" />
        <TextBlock x:Name="LapText" Grid.Row="1" Text="{Binding LapText}" FontSize="13" Margin="0,2,0,10"
                   Typography.NumeralAlignment="Tabular" Foreground="{DynamicResource Brush.TextSecondary}" />
        <UniformGrid Grid.Row="2" Columns="3" Margin="0,0,0,10">
            <Button x:Name="SplitButton" Content="{Binding SplitButtonText}" Click="SplitButton_Click"
                    Style="{StaticResource PrimaryButtonStyle}" Height="30" Margin="0,0,4,0" />
            <Button x:Name="FinishButton" Content="Finish" Click="FinishButton_Click"
                    Style="{StaticResource AppButtonStyle}" Height="30" Margin="0,0,4,0" />
            <Button x:Name="ResetButton" Content="Reset" Click="ResetButton_Click"
                    Style="{StaticResource AppButtonStyle}" Height="30" />
        </UniformGrid>
        <StackPanel Grid.Row="3" Margin="0,0,0,10">
            <TextBlock x:Name="HotkeyText" FontSize="11" Foreground="{DynamicResource Brush.TextMuted}"
                       TextWrapping="Wrap" />
            <TextBlock x:Name="UnavailableNote" FontSize="11" Margin="0,4,0,0" TextWrapping="Wrap"
                       Foreground="{DynamicResource Brush.AccentGold}" Visibility="Collapsed"
                       Text="A timer shortcut is unavailable. Choose different keys in Settings." />
        </StackPanel>
        <ListBox x:Name="LapsList" Grid.Row="4" ItemsSource="{Binding Laps}" Background="Transparent"
                 BorderThickness="0" HorizontalContentAlignment="Stretch"
                 VirtualizingPanel.IsVirtualizing="True" VirtualizingPanel.VirtualizationMode="Recycling"
                 ScrollViewer.HorizontalScrollBarVisibility="Disabled">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="40" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="{Binding Number}" FontSize="12"
                                   Foreground="{DynamicResource Brush.TextMuted}" />
                        <TextBlock Grid.Column="1" Text="{Binding LapTimeText}" FontSize="12"
                                   Typography.NumeralAlignment="Tabular"
                                   Foreground="{DynamicResource Brush.TextPrimary}" />
                        <TextBlock Grid.Column="2" Text="{Binding SplitTotalText}" FontSize="12"
                                   HorizontalAlignment="Right" Typography.NumeralAlignment="Tabular"
                                   Foreground="{DynamicResource Brush.TextMuted}" />
                    </Grid>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </Grid>
</UserControl>
```

`src/FourFoldAccountManager.Desktop/Views/TimerPanel.xaml.cs`:

```csharp
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using FourFoldAccountManager.Desktop.Services;

namespace FourFoldAccountManager.Desktop.Views;

public partial class TimerPanel : UserControl
{
    private TimerCoordinator? _coordinator;

    public TimerPanel()
    {
        InitializeComponent();
    }

    public void Attach(TimerCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        if (_coordinator is not null)
        {
            throw new InvalidOperationException("The timer panel is already attached to a timer.");
        }

        _coordinator = coordinator;
        DataContext = coordinator.Display;
        coordinator.Display.Laps.CollectionChanged += Laps_CollectionChanged;
    }

    public void SetHotkeys(string splitKeys, string finishKeys, string resetKeys, bool anyUnavailable)
    {
        HotkeyText.Text = $"Split {splitKeys} · Finish {finishKeys} · Reset {resetKeys}";
        UnavailableNote.Visibility = anyUnavailable ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SplitButton_Click(object sender, RoutedEventArgs e) => _coordinator?.Split();

    private void FinishButton_Click(object sender, RoutedEventArgs e) => _coordinator?.Finish();

    private void ResetButton_Click(object sender, RoutedEventArgs e) => _coordinator?.Reset();

    // Keep the newest lap in view as laps are recorded.
    private void Laps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { Count: > 0 } items)
        {
            LapsList.ScrollIntoView(items[items.Count - 1]);
        }
    }
}
```

- [ ] **Step 7: Add the Timer tab to the sidebar**

In `src/FourFoldAccountManager.Desktop/Views/PluginSidebar.xaml`, replace the whole `<UniformGrid Grid.Row="1" Columns="3" Margin="0,0,0,12">...</UniformGrid>` block with:

```xml
        <UniformGrid Grid.Row="1" Columns="2" Margin="0,0,0,12">
            <Button x:Name="XpTrackerButton" Content="XP" Tag="{x:Static core:PluginKind.XpTracker}"
                    Click="PluginButton_Click" Style="{StaticResource AppButtonStyle}" Height="30" Padding="4,0" Margin="0,0,4,4"
                    ToolTip="XP Tracker" />
            <Button x:Name="ClassComparisonButton" Content="Stats" Tag="{x:Static core:PluginKind.ClassComparison}"
                    Click="PluginButton_Click" Style="{StaticResource AppButtonStyle}" Height="30" Padding="4,0" Margin="0,0,0,4"
                    ToolTip="Class comparison" />
            <Button x:Name="XpCalculatorButton" Content="XP calc" Tag="{x:Static core:PluginKind.XpCalculator}"
                    Click="PluginButton_Click" Style="{StaticResource AppButtonStyle}" Height="30" Padding="4,0" Margin="0,0,4,0"
                    ToolTip="XP calculator" />
            <Button x:Name="TimerButton" Content="Timer" Tag="{x:Static core:PluginKind.Timer}"
                    Click="PluginButton_Click" Style="{StaticResource AppButtonStyle}" Height="30" Padding="4,0"
                    ToolTip="Speedrun timer" />
        </UniformGrid>
```

and add `<views:TimerPanel x:Name="TimerPanelView" Visibility="Collapsed" />` after `<views:ExperienceCalculatorPanel x:Name="XpCalculatorPanelView" Visibility="Collapsed" />`.

In `src/FourFoldAccountManager.Desktop/Views/PluginSidebar.xaml.cs`:

1. Add `using FourFoldAccountManager.Desktop.Services;`.
2. After `SetTrackerItemsSource`, add:

```csharp
    public void AttachTimer(TimerCoordinator coordinator) => TimerPanelView.Attach(coordinator);

    public void SetTimerHotkeys(string splitKeys, string finishKeys, string resetKeys, bool anyUnavailable) =>
        TimerPanelView.SetHotkeys(splitKeys, finishKeys, resetKeys, anyUnavailable);
```

3. In `UpdateActivePlugin`, add these lines alongside the existing visibility and opacity lines:

```csharp
        TimerPanelView.Visibility = ActivePlugin == PluginKind.Timer ? Visibility.Visible : Visibility.Collapsed;
```

```csharp
        TimerButton.Opacity = ActivePlugin == PluginKind.Timer ? 1d : 0.65d;
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`
Expected: PASS, including 7 `TimerCoordinatorTests`, 3 `TimerPanelTests`, and 1 `PluginSidebarTimerTests`. If a binding assertion in `TimerPanelTests` reads a stale value, call `panel.UpdateLayout()` before asserting. Do not weaken the assertion.

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: PASS, including `PluginSelectionPolicyTests`.

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 9: Commit**

```bash
git add -A src/FourFoldAccountManager.Core src/FourFoldAccountManager.Core.Tests src/FourFoldAccountManager.Desktop src/FourFoldAccountManager.Desktop.Tests
git commit -m "feat: add timer coordinator and sidebar Timer tab"
```

---

### Task 6: Timer overlay card and app wiring

**Files:**
- Modify: `src/FourFoldAccountManager.Core/Models/OverlayAddOnKind.cs`
- Modify: `src/FourFoldAccountManager.Core/Overlay/OverlayAddOnCatalog.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Resources/OverlayCardTemplates.xaml`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`
- Modify: `README.md`
- Test: `src/FourFoldAccountManager.Core.Tests/Overlay/OverlayCardPolicyTests.cs` (catalog test)
- Test: `src/FourFoldAccountManager.Desktop.Tests/Views/TimerOverlayCardTests.cs`

**Interfaces:**
- Consumes:
  - Task 4: `MainWindow.HandleGlobalShortcut` and `_shortcuts`.
  - Task 5: `TimerCoordinator` (`Display`, `ShortcutsSuspended`, `TryHandleShortcut`, `Dispose`), `PluginSidebar.AttachTimer`, and `PluginSidebar.SetTimerHotkeys`.
  - Task 3: `ShortcutText.Format`.
  - Existing overlay system: `OverlayAddOnCatalog`, `OverlayCardLayer`, `OverlayCardModel`, `MainWindow.CreateOverlayCardData`, and `WpfTestHost`.
- Produces:
  - `OverlayAddOnKind.Timer = 1`.
  - A catalog entry: Timer, Global, "Timer", 220×60 default, 150×44 minimum.
  - The `TimerDisplay` card template.

- [ ] **Step 1: Write the failing tests**

In `src/FourFoldAccountManager.Core.Tests/Overlay/OverlayCardPolicyTests.cs`, replace the test `CatalogRegistersOnlyTheAccountScopedXpCard` with these two tests:

```csharp
    [Fact]
    public void CatalogRegistersTheXpAndTimerCards()
    {
        Assert.Equal([OverlayAddOnKind.Xp, OverlayAddOnKind.Timer], OverlayAddOnCatalog.All.Select(d => d.Kind));

        var xp = OverlayAddOnCatalog.All[0];
        Assert.Equal(OverlayAddOnScope.Account, xp.Scope);
        Assert.Equal("XP/hr", xp.DisplayName);
        Assert.Equal((200d, 52d, 144d, 40d), (xp.DefaultWidth, xp.DefaultHeight, xp.MinimumWidth, xp.MinimumHeight));

        var timer = OverlayAddOnCatalog.All[1];
        Assert.Equal(OverlayAddOnScope.Global, timer.Scope);
        Assert.Equal("Timer", timer.DisplayName);
        Assert.Equal((220d, 60d, 150d, 44d),
            (timer.DefaultWidth, timer.DefaultHeight, timer.MinimumWidth, timer.MinimumHeight));

        Assert.Equal(1, (int)OverlayAddOnKind.Timer);
        Assert.False(OverlayAddOnCatalog.TryGet((OverlayAddOnKind)99, out _));
        Assert.Equal(-1, OverlayAddOnCatalog.IndexOf((OverlayAddOnKind)99));
    }

    [Fact]
    public void GlobalCardKeysMustNotNameAnAccount()
    {
        Assert.True(OverlayCardPolicy.IsValidKey(new OverlayCardKey(OverlayAddOnKind.Timer, null)));
        Assert.False(OverlayCardPolicy.IsValidKey(new OverlayCardKey(OverlayAddOnKind.Timer, Guid.NewGuid())));
    }
```

`src/FourFoldAccountManager.Desktop.Tests/Views/TimerOverlayCardTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Desktop.Services;
using FourFoldAccountManager.Desktop.Views;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Views;

public sealed class TimerOverlayCardTests
{
    [Fact]
    public void TimerCardRendersTheLiveTotalAndLapLineAtTopCentre() => WpfTestHost.Run(() =>
    {
        var clock = new ManualTimeProvider();
        using var coordinator = new TimerCoordinator(clock);
        OverlayAddOnCatalog.TryGet(OverlayAddOnKind.Timer, out var definition);
        var layer = new OverlayCardLayer { Width = 1000, Height = 500 };
        layer.Measure(new Size(1000, 500));
        layer.Arrange(new Rect(0, 0, 1000, 500));
        layer.UpdateLayout();
        var key = new OverlayCardKey(OverlayAddOnKind.Timer, null);

        layer.SetCards([new OverlayCardModel(key, definition!, null, 0, coordinator.Display)], editing: false);
        coordinator.Split();
        clock.Advance(TimeSpan.FromSeconds(2.5));
        coordinator.Refresh();
        layer.UpdateLayout();

        var texts = TextBlocks(layer.Frames[key]).Select(text => text.Text).ToArray();
        Assert.Contains("0:02.50", texts);
        Assert.Contains("Lap 1  0:02.50", texts);
        Assert.Equal(390, Canvas.GetLeft(layer.Frames[key]), 3);
    });

    private static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBlock text)
            {
                yield return text;
            }

            foreach (var nested in TextBlocks(child))
            {
                yield return nested;
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release --filter "FullyQualifiedName~OverlayCardPolicyTests"`
Expected: the build FAILS with `'OverlayAddOnKind' does not contain a definition for 'Timer'`.

- [ ] **Step 3: Register the Timer card**

In `src/FourFoldAccountManager.Core/Models/OverlayAddOnKind.cs`, change the enum body to:

```csharp
    Xp = 0,
    Timer = 1
```

In `src/FourFoldAccountManager.Core/Overlay/OverlayAddOnCatalog.cs`, add the Timer definition after the Xp definition in the `All` array:

```csharp
        new OverlayAddOnDefinition(OverlayAddOnKind.Xp, OverlayAddOnScope.Account, "XP/hr", 200, 52, 144, 40),
        new OverlayAddOnDefinition(OverlayAddOnKind.Timer, OverlayAddOnScope.Global, "Timer", 220, 60, 150, 44)
```

In `src/FourFoldAccountManager.Desktop/Resources/OverlayCardTemplates.xaml`, add this template after the XP template:

```xml
    <DataTemplate DataType="{x:Type views:TimerDisplay}">
        <Grid VerticalAlignment="Center">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>
            <TextBlock Text="{Binding TotalText}" FontSize="20" FontWeight="Bold"
                       Typography.NumeralAlignment="Tabular" Foreground="{DynamicResource Brush.AccentGold}"
                       TextTrimming="CharacterEllipsis" />
            <TextBlock Grid.Row="1" Text="{Binding LapText}" FontSize="11"
                       Typography.NumeralAlignment="Tabular" Foreground="{DynamicResource Brush.TextSecondary}"
                       TextTrimming="CharacterEllipsis" />
        </Grid>
    </DataTemplate>
```

- [ ] **Step 4: Wire the timer into `MainWindow`**

In `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`:

1. **Field.** After `private readonly DispatcherTimer _leaderboardRefreshTimer = ...;`, add:

```csharp
    private readonly TimerCoordinator _timer = new();
```

2. **Constructor.** After `PluginSidebar.SetTrackerItemsSource(_xpTrackerRows);`, add:

```csharp
        PluginSidebar.AttachTimer(_timer);
```

3. **Timer shortcuts.** In `HandleGlobalShortcut`, replace `default:` / `return false;` with:

```csharp
            default:
                return _timer.TryHandleShortcut(action);
```

4. **Overlay data.** Change `CreateOverlayCardData` from `private static` to `private`, and add the Timer arm before `_ => null`:

```csharp
            OverlayAddOnKind.Timer => _timer.Display,
```

5. **Hotkey text.** Add this method directly after `HandleGlobalShortcut`:

```csharp
    private void UpdateTimerHotkeys()
    {
        GlobalShortcutAction[] timerActions =
            [GlobalShortcutAction.TimerSplit, GlobalShortcutAction.TimerFinish, GlobalShortcutAction.TimerReset];
        PluginSidebar.SetTimerHotkeys(
            ShortcutText.Format(_panelSettings.TimerSplitShortcut),
            ShortcutText.Format(_panelSettings.TimerFinishShortcut),
            ShortcutText.Format(_panelSettings.TimerResetShortcut),
            timerActions.Any(action => _shortcuts?.IsAvailable(action) != true));
    }
```

6. **`MainWindow_Loaded`.** Directly after `_shortcuts?.Initialize(_panelSettings);`, add:

```csharp
            UpdateTimerHotkeys();
```

7. **`Settings_Click`.** Replace:

```csharp
        if (dialog.ShowDialog() != true)
        {
            return;
        }
```

with:

```csharp
        // Recording a shortcut must not split, finish, or reset a live run.
        _timer.ShortcutsSuspended = true;
        bool? accepted;
        try
        {
            accepted = dialog.ShowDialog();
        }
        finally
        {
            _timer.ShortcutsSuspended = false;
        }

        if (accepted != true)
        {
            return;
        }
```

   Then, in the same method, replace:

```csharp
            UpdateManageSlotsButton();
            GlobalStatusText.Text = dialog.ResetLayoutSizes
```

   with:

```csharp
            UpdateManageSlotsButton();
            UpdateTimerHotkeys();
            GlobalStatusText.Text = dialog.ResetLayoutSizes
```

8. **`CompleteShutdown`.** Directly after the `shortcuts?.Dispose();` try/catch block, add:

```csharp
        _timer.Dispose();
```

`MainWindow.xaml.cs` already has `using FourFoldAccountManager.Desktop.Services;`, `using FourFoldAccountManager.Desktop.Views;`, and `using FourFoldAccountManager.Core.Models;`.

- [ ] **Step 5: Document the Timer**

In `README.md`, add this paragraph immediately after the paragraph that contains "Tracker behavior remains available inside the Plugins sidebar.":

```
The **Timer** plugin is a speedrun stopwatch. Press the split shortcut to start a run and again to record each lap, the finish shortcut to stop, and the reset shortcut (or **Reset**) to clear it. The shortcuts default to Ctrl+Alt+Shift+S, F, and R and can be changed in Settings, including to a single numpad, F13–F24, Pause, Scroll Lock, or Insert key. A key bound on its own stops reaching games and other apps while FourFold is open. In full screen, switch the Timer card on in the Overlays panel and drag it anywhere. Runs are not saved when the app closes.
```

- [ ] **Step 6: Build and run every suite**

Run: `dotnet build FourFoldAccountManager.sln -c Release`
Expected: `0 Warning(s)`, `0 Error(s)`.

Run: `dotnet test src/FourFoldAccountManager.Core.Tests -c Release`
Expected: PASS.

Run: `dotnet test src/FourFoldAccountManager.Desktop.Tests -c Release`
Expected: PASS, including `TimerOverlayCardTests`.

Run: `dotnet test src/FourFoldAccountManager.Leaderboard.Tests -c Release --filter "FullyQualifiedName!~LeaderboardStoreTests&FullyQualifiedName!~Readiness"`
Expected: PASS. This is a regression check only.

- [ ] **Step 7: Publish a test EXE for the manual check**

```bash
dotnet publish src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "$TEMP/fourfold-timer-test"
```

The user performs these checks. Report them as not performed if you cannot drive the GUI.

1. In Settings, bind Timer split to Numpad1 (Num Lock on). With a game client focused, press Numpad1 to start, press it again twice for laps, then press the finish shortcut. The Timer tab shows 2 laps and a frozen total.
2. Press Reset. Everything clears, and the Split button reads **Start**.
3. In full screen, open the Overlays panel. The Global section shows a **Timer** switch. Switch it on: the card appears at the top centre, updates while running, and can be dragged across the line between clients.
4. Try to bind Timer finish to the same keys as Reveal overlays tab. Save shows "Reveal overlays tab and Timer finish use the same keys…".
5. With Settings open and a run timing, press the split shortcut. The run does not change.

- [ ] **Step 8: Commit**

```bash
git add -A src/FourFoldAccountManager.Core src/FourFoldAccountManager.Core.Tests src/FourFoldAccountManager.Desktop src/FourFoldAccountManager.Desktop.Tests README.md
git commit -m "feat: add Timer overlay card and wire the timer into the app"
```
