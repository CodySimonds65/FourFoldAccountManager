# Fullscreen XP Overlay Edge-Tab Visibility and Shortcut Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hide the fullscreen XP overlay edge arrow after Done and reveal it only after a successfully applied layout change or the configured global shortcut.

**Architecture:** Keep dismissal as transient fullscreen-tray state, persist a typed key/modifier chord in `PanelSettings`, and register it against the manager window with Windows `RegisterHotKey`. A WPF-free coordinator handles registration replacement transactionally so conflicts or settings-save failures leave the prior binding intact.

**Tech Stack:** .NET 10, WPF, Windows `RegisterHotKey`/`WM_HOTKEY`, System.Text.Json, xUnit local-only harness.

**Spec:** `docs/superpowers/specs/2026-09-22-xp-overlay-edge-tab-hotkey-design.md` (amends `docs/superpowers/specs/2026-09-22-one-above-three-xp-overlays-design.md`).

**Status:** Awaiting implementation-plan review; no product code for this addendum has been changed.

## Global Constraints

- Keep all automated tests in ignored `.local-tests/`; do not stage, commit, or push their project, source, or dependencies.
- The default chord is `Ctrl+Alt+Shift+O`; persist virtual-key and modifier values, not localized display text.
- Use `RegisterHotKey`/`WM_HOTKEY`; do not add a low-level keyboard hook.
- A successful layout change and the global shortcut reveal only the edge arrow; neither opens the tray.
- Done hides the edge arrow for the current app session; ordinary refreshes and fullscreen exit/re-entry do not reveal it.
- On registration conflict, preserve the prior saved and active chord; a startup conflict must not disable overlays or layout-based reveal.

## Review Focus

1. A legacy settings file with no shortcut property must load the default chord without changing other settings (Task 1 test).
2. An invalid persisted key/modifier combination must use the established settings-validation failure path rather than reaching Win32 registration (Task 1 load test).
3. A shortcut owned by another process must leave the previous registration active and the saved value unchanged (Task 3 test).
4. A settings write failure after candidate registration must unregister the candidate and retain the old binding (Task 3 test).
5. Dismissal must survive fullscreen exit/re-entry and unrelated refreshes, while layout change/shortcut reveal without opening the tray—even if the pointer is already over the edge tab (Task 2 unit test and Task 5 smoke check).

---

### Task 1: Persist and validate the reveal chord

**Files:**
- Create: `src/FourFoldAccountManager.Core/Models/GlobalHotkeyChord.cs`
- Modify: `src/FourFoldAccountManager.Core/Models/PanelSettings.cs`
- Modify: `src/FourFoldAccountManager.Core/Data/SettingsStore.cs`
- Modify: `src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs`
- Test (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/GlobalHotkeyChordTests.cs`
- Test (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/SettingsStoreTests.cs`
- Test (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/PanelLayoutPolicyTests.cs`

**Interfaces:**
- Produces `GlobalHotkeyModifiers` (`[Flags]`: `Control=1`, `Alt=2`, `Shift=4`) and `GlobalHotkeyChord(ushort VirtualKey, GlobalHotkeyModifiers Modifiers)`.
- `GlobalHotkeyChord.DefaultRevealXpOverlayTab` is virtual key `0x4F` (`O`) with all three supported modifiers. `IsValid` requires a non-modifier virtual key in `0x20..0xFE`, at least one supported modifier, and no unknown modifier bits. `TryCreate(ushort virtualKey, GlobalHotkeyModifiers modifiers, out GlobalHotkeyChord chord)` returns false for an invalid pair.
- `PanelSettings.RevealXpOverlayTabShortcut` defaults to `GlobalHotkeyChord.DefaultRevealXpOverlayTab`.
- `SettingsStore.Validate` rejects an invalid chord using `InvalidDataException`, and copies a valid chord into normalized settings. `WithLayout`, `Assign`, and `ClearAccount` preserve it.

- [ ] **Step 1: Write the failing Core tests**

Add `GlobalHotkeyChordTests` with this first test and add tests named `LoadAsync_defaults_missing_reveal_shortcut_for_legacy_settings`, `SaveAsync_and_LoadAsync_round_trip_reveal_shortcut`, `LoadAsync_rejects_invalid_reveal_shortcut`, `SaveAsync_rejects_invalid_reveal_shortcut`, and `Layout_and_assignment_changes_preserve_reveal_shortcut`.

```csharp
[Fact]
public void Default_uses_ctrl_alt_shift_o()
{
    Assert.Equal(
        new GlobalHotkeyChord(0x4F,
            GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt | GlobalHotkeyModifiers.Shift),
        PanelSettings.Default.RevealXpOverlayTabShortcut);
}
```

The remaining tests assert legacy compatibility, a custom chord round trip, rejection of an unknown modifier bit and modifier-only virtual key on both load and save, and preservation through `WithLayout`, `Assign`, and `ClearAccount`.

- [ ] **Step 2: Run the focused tests and verify the expected failure**

Run: `dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter "FullyQualifiedName~GlobalHotkeyChordTests|FullyQualifiedName~reveal_shortcut"`

Expected: compile/test failure because the chord type/property and persistence behavior do not exist yet.

- [ ] **Step 3: Implement the Core chord and persistence**

Implement the flags enum and immutable chord record. Add the defaulted optional settings property. Copy it through `SettingsStore.Validate` and all `PanelLayoutPolicy` methods that construct a new `PanelSettings`; reject null/invalid persisted chord data.

```csharp
public sealed record GlobalHotkeyChord(ushort VirtualKey, GlobalHotkeyModifiers Modifiers)
{
    public static GlobalHotkeyChord DefaultRevealXpOverlayTab { get; } =
        new(0x4F, GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt | GlobalHotkeyModifiers.Shift);

    public bool IsValid =>
        VirtualKey is >= 0x20 and <= 0xFE &&
        VirtualKey is not (0x5B or 0x5C) &&
        Modifiers != GlobalHotkeyModifiers.None &&
        (Modifiers & ~(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt | GlobalHotkeyModifiers.Shift)) == 0;

    public static bool TryCreate(ushort virtualKey, GlobalHotkeyModifiers modifiers, out GlobalHotkeyChord chord)
    {
        chord = new GlobalHotkeyChord(virtualKey, modifiers);
        return chord.IsValid;
    }
}
```

Do not add a project dependency.

- [ ] **Step 4: Re-run focused and complete Core tests**

Run: `dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj`

Expected: the new tests and the complete local Core suite pass.

- [ ] **Step 5: Commit only tracked Core files**

```powershell
git add src/FourFoldAccountManager.Core/Models/GlobalHotkeyChord.cs src/FourFoldAccountManager.Core/Models/PanelSettings.cs src/FourFoldAccountManager.Core/Data/SettingsStore.cs src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs
git commit -m "feat: persist XP overlay reveal shortcut"
```

Never stage `.local-tests/`.

### Task 2: Model and apply edge-tab dismissal

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTabVisibilityState.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTray.xaml.cs`
- Modify local-only project: `.local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj`
- Test (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/FullscreenXpOverlayTabVisibilityStateTests.cs`

**Interfaces:**
- `FullscreenXpOverlayTabVisibilityState.IsVisible(bool isFullScreen)` returns `isFullScreen && !_dismissed`.
- `Dismiss()` records the session dismissal; `Reveal()` clears it. A fresh state object starts revealed.
- The local test project source-links this WPF-free production file so the production state logic is tested without adding a tracked test project or product test dependency.
- `FullscreenXpOverlayTray.DismissEdgeTab()` closes the tray and hides the edge button. `RevealEdgeTab()` shows the button if fullscreen but never opens the tray or changes edit mode.

- [ ] **Step 1: Add tests for initial visibility and dismissal persistence**

Add `FullscreenXpOverlayTabVisibilityStateTests` with:

```csharp
[Fact]
public void Dismissal_survives_fullscreen_exit_and_reentry_until_revealed()
{
    var state = new FullscreenXpOverlayTabVisibilityState();
    Assert.True(state.IsVisible(isFullScreen: true));
    state.Dismiss();
    Assert.False(state.IsVisible(isFullScreen: true));
    Assert.False(state.IsVisible(isFullScreen: false));
    Assert.False(state.IsVisible(isFullScreen: true));
    state.Reveal();
    Assert.True(state.IsVisible(isFullScreen: true));
}
```

Also assert a fresh instance is visible in fullscreen and that non-fullscreen is never visible.

Add this source-link entry to the ignored local test project so tests compile the production state file directly:

```xml
<Compile Include="..\..\src\FourFoldAccountManager.Desktop\Views\FullscreenXpOverlayTabVisibilityState.cs"
         Link="ProductionSources\FullscreenXpOverlayTabVisibilityState.cs" />
```

- [ ] **Step 2: Run the visibility tests and verify the expected failure**

Run: `dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~FullscreenXpOverlayTabVisibilityStateTests`

Expected: compile failure because the state type is absent.

- [ ] **Step 3: Implement the WPF-free visibility state and tray methods**

Add the state class with an initially false `_dismissed` flag:

```csharp
internal sealed class FullscreenXpOverlayTabVisibilityState
{
    private bool _dismissed;

    public bool IsVisible(bool isFullScreen) => isFullScreen && !_dismissed;
    public void Dismiss() => _dismissed = true;
    public void Reveal() => _dismissed = false;
}
```

Use it when `SetFullscreen` updates edge-tab visibility; do not reset it on fullscreen entry or exit. Make Done call `DismissEdgeTab()` before raising `DoneRequested`. Keep `RevealEdgeTab()` independent of tray visibility and edit mode.

- [ ] **Step 4: Run the visibility tests and solution build**

Run: `dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~FullscreenXpOverlayTabVisibilityStateTests`

Then run: `dotnet build FourFoldAccountManager.sln`

Expected: focused tests pass and the solution builds without warnings/errors.

- [ ] **Step 5: Commit the tray and state implementation**

```powershell
git add src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTabVisibilityState.cs src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTray.xaml.cs
git commit -m "feat: hide fullscreen XP overlay tab after done"
```

The source link and test file remain local and unstaged.

### Task 3: Add transactional global-hotkey registration

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Services/GlobalHotkeyRegistrationCoordinator.cs`
- Create: `src/FourFoldAccountManager.Desktop/Services/WindowsGlobalHotkeyRegistrar.cs`
- Modify local-only project: `.local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj`
- Test (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/GlobalHotkeyRegistrationCoordinatorTests.cs`

**Interfaces:**
- `IGlobalHotkeyRegistrar.TryRegister(int id, GlobalHotkeyChord chord)` returns false without taking ownership if Windows rejects the chord; `Unregister(int id)` releases that ID.
- `GlobalHotkeyRegistrationCoordinator.TryInitialize(GlobalHotkeyChord chord)` registers and commits the startup chord or returns false on conflict.
- `Task<bool> TryReplaceAsync(GlobalHotkeyChord chord, Func<Task> persist)` registers a candidate while the current chord stays active, runs persistence, then commits the candidate and releases the prior ID. It returns false on registration conflict. If persistence throws, it cancels the candidate and rethrows. If the chord equals the currently active chord, it runs persistence without registering a duplicate; after startup registration conflict, it still retries registration.
- `IsCurrent(int id)` filters `WM_HOTKEY` messages. `Dispose()` unregisters active and pending IDs exactly once; an in-flight replacement completing after disposal returns false and cannot reactivate a chord.
- `WindowsGlobalHotkeyRegistrar` maps Control/Alt/Shift to Win32 flags, adds `MOD_NOREPEAT`, and wraps `RegisterHotKey`/`UnregisterHotKey` for the manager HWND.

- [ ] **Step 1: Add failing tests with a fake registrar**

Add tests named `Conflict_keeps_current_registration`, `Successful_replace_persists_before_releasing_current`, `Persistence_failure_cancels_candidate_and_keeps_current`, `Replacing_with_same_chord_does_not_register_a_duplicate`, and `Dispose_unregisters_active_and_pending_ids`. Source-link the WPF-free coordinator into the local test project. The fake records registered IDs, can reject its next registration, and exposes whether an ID is active. The successful-replace test asserts that the old ID is active inside the persistence callback and removed only after it returns; the failure test throws `IOException` from the callback and asserts the old ID remains active and the candidate is removed.

```csharp
[Fact]
public async Task Conflict_keeps_current_registration()
{
    var registrar = new FakeGlobalHotkeyRegistrar();
    using var coordinator = new GlobalHotkeyRegistrationCoordinator(registrar);
    Assert.True(coordinator.TryInitialize(DefaultChord));
    var activeId = registrar.ActiveIds.Single();
    registrar.RejectNextRegistration = true;

    var replaced = await coordinator.TryReplaceAsync(AlternateChord, () => Task.CompletedTask);

    Assert.False(replaced);
    Assert.Equal(new[] { activeId }, registrar.ActiveIds);
}
```

For shutdown during a pending replace, hold the persistence callback on two `TaskCompletionSource` instances: start `TryReplaceAsync`, wait until the callback begins, dispose the coordinator, release persistence, then assert the replacement returns false and the fake registrar has no active IDs. Also assert an unchanged chord increments the persistence count but not the registrar call count.

Use this source-link entry in the ignored local test project:

```xml
<Compile Include="..\..\src\FourFoldAccountManager.Desktop\Services\GlobalHotkeyRegistrationCoordinator.cs"
         Link="ProductionSources\GlobalHotkeyRegistrationCoordinator.cs" />
```

- [ ] **Step 2: Run the focused coordinator tests and verify the expected failure**

Run: `dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~GlobalHotkeyRegistrationCoordinatorTests`

Expected: compile failure because the coordinator and registrar contract are absent.

- [ ] **Step 3: Implement the coordinator and Windows adapter**

Keep replacement/rollback logic in the WPF-free coordinator. The adapter must pass only supported modifier bits plus `MOD_NOREPEAT` to `RegisterHotKey`, and treat a normal Windows registration failure as a conflict result rather than an exception.

```csharp
if (!_registrar.TryRegister(candidateId, chord))
{
    return false;
}

try
{
    await persist();
}
catch
{
    _registrar.Unregister(candidateId);
    throw;
}

Commit(candidateId);
return true;
```

- [ ] **Step 4: Run focused coordinator tests and the solution build**

Run the focused test command from Step 2, then `dotnet build FourFoldAccountManager.sln`.

Expected: all coordinator tests pass and the Windows adapter compiles in the desktop project.

- [ ] **Step 5: Commit only tracked service files**

```powershell
git add src/FourFoldAccountManager.Desktop/Services/GlobalHotkeyRegistrationCoordinator.cs src/FourFoldAccountManager.Desktop/Services/WindowsGlobalHotkeyRegistrar.cs
git commit -m "feat: register global XP overlay shortcut"
```

### Task 4: Configure the shortcut and connect reveal triggers

**Files:**
- Modify: `src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml`
- Modify: `src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml.cs`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`
- Modify: `src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTray.xaml.cs`
- Modify if needed: `src/FourFoldAccountManager.Desktop/Views/XpOverlayLayer.cs` (existing, uncommitted slot-drop integration from the original overlay task)
- Test (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/GlobalHotkeyRegistrationCoordinatorTests.cs`
- Test (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/FullscreenXpOverlayTabVisibilityStateTests.cs`

**Interfaces:**
- `SettingsDialog` accepts the current chord and registration-availability status and returns the captured `GlobalHotkeyChord` with the existing settings values.
- The key recorder accepts Control/Alt/Shift plus one non-modifier key, displays a canonical chord, rejects unsupported/no-modifier input, and treats Escape during capture as cancel.
- `MainWindow` installs an `HwndSource` hook after `SourceInitialized`, initializes registration after settings load, and calls `RevealEdgeTab()` only for a current `WM_HOTKEY` ID.
- On shortcut replacement, `MainWindow` passes the complete settings write through `TryReplaceAsync`; on conflict, show an error and keep the old setting/registration. On startup conflict, keep the app running and show the unavailable state in Settings.
- After `LayoutPicker_SelectionChanged` has saved and rebuilt successfully, reveal the edge tab. Do not reveal on failed selection, tracker refresh, account reassignment, or fullscreen entry.
- During normal shutdown dispose the hotkey coordinator before the final `Close()`; repeated close/error paths must not leave registration active.

- [ ] **Step 1: Implement the Settings chord recorder**

Add the compact **Reveal XP overlay tab** control to the existing full-screen Settings section. Initialize it from `PanelSettings.RevealXpOverlayTabShortcut`. While capturing, use `KeyInterop.VirtualKeyFromKey` and `Keyboard.Modifiers`; when `KeyEventArgs.Key` is `Key.System`, use `KeyEventArgs.SystemKey`. Map only Control/Alt/Shift, reject Windows-key combinations, and require one non-modifier key. Escape cancels capture. Display modifier names in Control, Alt, Shift order and then the key name.

```csharp
private void SettingsDialog_PreviewKeyDown(object sender, KeyEventArgs e)
{
    if (!_isCapturingRevealShortcut)
    {
        return;
    }

    e.Handled = true;
    var key = e.Key == Key.System ? e.SystemKey : e.Key;
    if (key == Key.Escape)
    {
        _isCapturingRevealShortcut = false;
        return;
    }

    var virtualKey = (ushort)KeyInterop.VirtualKeyFromKey(key);
    var modifiers = MapSupportedModifiers(Keyboard.Modifiers);
    if (GlobalHotkeyChord.TryCreate(virtualKey, modifiers, out var chord))
    {
        RevealShortcut = chord;
        _isCapturingRevealShortcut = false;
    }
}
```

`MapSupportedModifiers(ModifierKeys modifiers)` returns `GlobalHotkeyModifiers.None` when the Windows modifier is present; otherwise it maps the three supported flags individually.

- [ ] **Step 2: Connect setting save, registration, and conflict feedback**

In `Settings_Click`, extract the existing settings update body into a local `PersistDialogSettingsAsync()` function. Keep its reset-layout and scaling rollback behavior, and add `RevealXpOverlayTabShortcut = dialog.RevealXpOverlayTabShortcut` to the candidate settings. Pass the local function as the `persist` callback. On false, show a message that Windows could not register the chord, that the previous shortcut remains active, and that the user should choose another. On save exception, let the coordinator cancel the candidate and retain the previous registration; surface the existing safe settings-save error.

```csharp
var registered = await _hotkeyCoordinator.TryReplaceAsync(
    dialog.RevealXpOverlayTabShortcut,
    PersistDialogSettingsAsync);
```

The candidate settings must include:

```csharp
candidate = candidate with
{
    FillGameToPanel = dialog.FillGameToPanel,
    ShowFullScreenExitButton = dialog.ShowFullScreenExitButton,
    RevealXpOverlayTabShortcut = dialog.RevealXpOverlayTabShortcut
};
```

- [ ] **Step 3: Connect native messages, layout success, and shutdown**

Attach the Win32 message hook to the MainWindow HWND after `SourceInitialized`. Initialize the saved chord after settings load; on failure, set the Settings availability indicator and continue. When `WM_HOTKEY` has the active ID, call `FullscreenXpOverlayTray.RevealEdgeTab()` and mark the message handled. After successful layout save and rebuild, call `RevealEdgeTab()`; leave failed layout selection unchanged. Dispose registrations in every final shutdown path.

- [ ] **Step 4: Run focused local tests and build**

Run: `dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj`

Then run: `dotnet build FourFoldAccountManager.sln`

Expected: all local tests pass and the solution builds without warnings/errors.

- [ ] **Step 5: Commit only tracked Settings/MainWindow files**

```powershell
git add src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml.cs src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTray.xaml.cs src/FourFoldAccountManager.Desktop/Views/XpOverlayLayer.cs
git commit -m "feat: configure fullscreen overlay reveal shortcut"
```

If a listed file has no changes, omit it. Never stage the local test harness.

### Task 5: Run integrated verification

**Files:**
- Verify: `FourFoldAccountManager.sln`
- Verify only: `.local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj`

**Interfaces:**
- Consumes the current one-above-three/overlay integration and Tasks 1–4.
- Produces a clean Release build, passing local-only tests, and a recorded manual UI result.

- [ ] **Step 1: Run Release build and local tests**

Run: `dotnet build FourFoldAccountManager.sln -c Release`

Then run: `dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj -c Release`

Expected: both succeed with no warnings/errors and all tests passing.

- [ ] **Step 2: Smoke-test fullscreen visibility without launching an account**

In the local desktop app, record the current layout and shortcut first. Verify: enter fullscreen and see the arrow; open the tray and choose Done; confirm both arrow and tray disappear; exit and re-enter fullscreen and confirm it remains hidden; change layout and enter fullscreen to confirm the arrow returns but tray stays closed; activate the arrow to open the tray. Press the default shortcut while the manager is not foreground, including once with the pointer already at the edge tab, and confirm it reveals only the arrow. Restore the original layout and shortcut before closing. Do not launch or authenticate any saved account.

- [ ] **Step 3: Verify Settings capture and conflict feedback safely**

Verify the Settings field displays the saved chord and captures an alternate chord. Use an isolated settings root if the app provides one; otherwise cancel without saving an alternate chord to the user’s real profile. Registration-conflict and settings-save rollback are covered by the fake-registrar tests. If an actual game client is already open, check the default chord while its WebView has focus; do not launch or authenticate an account for this check.

- [ ] **Step 4: Confirm local tests cannot enter the branch**

Run: `git check-ignore -v .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj`

Then run: `git status --short` and `git diff --cached --name-only`.

Expected: `.local-tests/` is ignored and no local test project/source/dependency is staged or tracked. Record any unavailable game-focus check accurately; do not claim it was verified.

---

## Execution notes

The tasks are sequential: persisted chord types precede the UI and registration; the visibility state and registration coordinator are independently testable before MainWindow wiring. Continue in the existing isolated feature worktree. Preserve the in-progress `MainWindow.xaml.cs` and `XpOverlayLayer.cs` integration edits; do not reset, revert, or stage unrelated changes. The existing local test harness and all new test source remain ignored and local-only. Execution is inline in the current task, as requested; pause after this written plan for user review before touching product code.
