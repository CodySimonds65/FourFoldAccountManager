# One-Above-Three Layout and Fullscreen XP Overlays Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the one-above-three four-client layout and independently placed, resizable, click-through XP/hr overlays for fullscreen play.

**Architecture:** Extend the existing `PanelLayoutPolicy` tree and settings model. Render one WPF overlay layer inside each client `BrowserHost`, bind its value to the existing XP tracker, and expose editing through a compact fullscreen edge-tab/tray. Persist normalized bounds per account ID so placements follow their accounts and scale with the client viewport.

**Tech Stack:** .NET 10, C#, WPF, `WebView2CompositionControl`, `System.Text.Json`, and a local-only xUnit project targeting .NET 10.

**Spec:** `docs/superpowers/specs/2026-09-22-one-above-three-xp-overlays-design.md`

## Global Constraints

- The new layout is presented as **1 × 3 · One above three**: slot 0 occupies the top row; slots 1, 2, and 3 occupy the lower row from left to right. This is four visible clients.
- Fullscreen has a compact auto-hide tab at the screen edge, not an XP button beside Exit.
- Each account retains its own size and position.
- The standard XP tracker panel remains the non-fullscreen experience; these floating overlays are fullscreen-only.
- Keep overlays in the existing WPF window and slot visual trees; do not add separate topmost overlay windows.
- Run local tests locally, but do not add the test project, its source, or its dependencies to tracked changes, commits, or the feature PR.
- Preserve old settings files by defaulting missing overlay-placement data to an empty map.

## Review Focus

- Legacy `settings.json` without an overlay map must load with no placements; pin this in Task 2's local `SettingsStore` test.
- Non-finite, empty, or out-of-viewport persisted bounds must be rejected; pin this in Task 2's local bounds/settings tests.
- A drop or resize at a client edge must remain visible and respect the minimum usable size; cover normalized bounds in Task 2 and actual-DIP edge behavior in Task 4's desktop check.
- Changing layout or reassigning an account must retain that account's placement, while deleting the account removes it; pin this in Task 2's `ClearAccount` test and Task 3's layout-preservation test.
- Missing XP rate data must display `— XP/hr`, and fullscreen exit must hide overlays and restore the tracker; verify the existing formatter path in Task 6 and the complete UI flow in Task 7.

## File Map

- `src/FourFoldAccountManager.Core/Models/PanelLayout.cs` — add the layout enum value.
- `src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs` — define its tree, dimensions, slot placements, split defaults, and account-keyed overlay settings operations.
- `src/FourFoldAccountManager.Core/Models/XpOverlayBounds.cs` — normalized, validated per-account bounds.
- `src/FourFoldAccountManager.Core/Models/PanelSettings.cs` and `src/FourFoldAccountManager.Core/Data/SettingsStore.cs` — persist and validate bounds compatibly.
- `src/FourFoldAccountManager.Desktop/Views/XpOverlayCard.xaml` and `.xaml.cs` — compact XP/hr card and edit affordances.
- `src/FourFoldAccountManager.Desktop/Views/XpOverlayLayer.cs` — per-slot Canvas, normalized/DIP conversion, drag target, and bounds events.
- `src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTray.xaml` and `.xaml.cs` — edge tab, account tray, drag source, and Done action.
- `src/FourFoldAccountManager.Desktop/Views/XpOverlayAccountChoice.cs` — account ID, account label, and XP/hr text used by the tray.
- `src/FourFoldAccountManager.Desktop/MainWindow.xaml` and `.xaml.cs` — host the edge tab and connect layouts, slot layers, tracker updates, fullscreen transitions, and settings saves.
- `.local-tests/FourFoldAccountManager.Core.Tests/` and `.git/info/exclude` — local-only test harness and local ignore rule; never stage these paths.

---

### Task 1: Prepare the local-only Core test harness

**Files:**
- Create (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj`
- Create (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/PanelLayoutPolicyTests.cs`
- Create (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/SettingsStoreTests.cs`
- Modify (local-only): `.git/info/exclude`

**Interfaces:**
- Consumes: `src/FourFoldAccountManager.Core/FourFoldAccountManager.Core.csproj`.
- Produces: `dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj` as a local-only command. No tracked project or solution will reference it.

- [ ] **Step 1: Add the local exclusion before creating test files**

Add this repository-root pattern to `.git/info/exclude`:

```gitignore
/.local-tests/
```

Verify it applies before proceeding:

```powershell
git check-ignore -v .local-tests/FourFoldAccountManager.Core.Tests
```

Expected: the output identifies `.git/info/exclude` and the `/.local-tests/` rule.

- [ ] **Step 2: Create the local .NET 10 xUnit project and Core reference**

```powershell
dotnet new xunit --framework net10.0 --output .local-tests/FourFoldAccountManager.Core.Tests
dotnet add .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj reference src/FourFoldAccountManager.Core/FourFoldAccountManager.Core.csproj
```

Delete the template-generated `UnitTest1.cs` with the patch tool; use the named test files in this plan instead.

- [ ] **Step 3: Run the empty harness once**

```powershell
dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj
```

Expected: PASS. Keep generated files and all later tests under `.local-tests/`; do not add them to the solution or stage them.

- [ ] **Step 4: Confirm local-only status**

```powershell
git check-ignore -v .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj
git status --short
```

Expected: the test project is ignored and no test path appears as a tracked or untracked change. This task intentionally has no commit.

### Task 2: Add normalized overlay bounds and settings persistence

**Files:**
- Create: `src/FourFoldAccountManager.Core/Models/XpOverlayBounds.cs`
- Modify: `src/FourFoldAccountManager.Core/Models/PanelSettings.cs`
- Modify: `src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs`
- Modify: `src/FourFoldAccountManager.Core/Data/SettingsStore.cs`
- Test (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/XpOverlayBoundsTests.cs`
- Test (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: `PanelSettings`, `SettingsStore`, and the local Core test harness from Task 1.
- Produces:
  - `public sealed record XpOverlayBounds(double X, double Y, double Width, double Height)` with `bool IsValid`.
  - `XpOverlayBounds.ClampToViewport()` returning a finite rectangle clamped to the normalized unit viewport, and rejecting non-finite coordinates or non-positive dimensions.
  - `PanelSettings.XpOverlayBoundsByAccount`, an `IReadOnlyDictionary<Guid, XpOverlayBounds>` defaulting to an empty dictionary.
  - `PanelLayoutPolicy.GetXpOverlayBounds(PanelSettings settings, Guid accountId)` returning nullable bounds.
  - `PanelLayoutPolicy.WithXpOverlayBounds(PanelSettings settings, Guid accountId, XpOverlayBounds bounds)` returning a copied settings record.
  - `PanelLayoutPolicy.ClearAccount` removes bounds for the deleted account; `WithLayout`, `Assign`, and settings-copy paths preserve the map.

- [ ] **Step 1: Add failing local bounds and settings tests**

In `XpOverlayBoundsTests.cs`, start with these cases:

```csharp
[Fact]
public void IsValid_accepts_finite_positive_bounds_inside_the_unit_viewport()
{
    Assert.True(new XpOverlayBounds(0.1, 0.2, 0.3, 0.4).IsValid);
}

[Fact]
public void IsValid_rejects_bounds_that_extend_past_the_viewport()
{
    Assert.False(new XpOverlayBounds(0.8, 0.2, 0.3, 0.4).IsValid);
}

[Fact]
public void IsValid_rejects_non_finite_values()
{
    Assert.False(new XpOverlayBounds(double.NaN, 0.2, 0.3, 0.4).IsValid);
    Assert.False(new XpOverlayBounds(0.1, 0.2, double.PositiveInfinity, 0.4).IsValid);
}

[Fact]
public void ClampToViewport_keeps_the_rectangle_inside_the_unit_viewport()
{
    var bounds = new XpOverlayBounds(0.9, 0.85, 0.3, 0.2).ClampToViewport();

    Assert.Equal(0.7, bounds.X, 3);
    Assert.Equal(0.8, bounds.Y, 3);
    Assert.True(bounds.IsValid);
}
```

Add `WithXpOverlayBounds`/`GetXpOverlayBounds` tests for two different account IDs, a `Guid.Empty` rejection test, and a `ClearAccount` test that proves only the removed account's placement is removed. Run:

```powershell
dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~XpOverlayBoundsTests
```

Expected: FAIL because the model and policy API do not exist yet.

- [ ] **Step 2: Implement the bounds value type**

Create the record and validate every component as finite, `X` and `Y` within `[0, 1]`, `Width` and `Height` greater than zero and at most one, and `X + Width <= 1`, `Y + Height <= 1`. Implement `ClampToViewport()` by rejecting non-finite coordinates or non-positive dimensions, limiting width/height to 1, then clamping X/Y to `[0, 1 - Width]` and `[0, 1 - Height]`:

```csharp
public bool IsValid =>
    double.IsFinite(X) && double.IsFinite(Y) &&
    double.IsFinite(Width) && double.IsFinite(Height) &&
    X >= 0 && Y >= 0 && Width > 0 && Height > 0 &&
    Width <= 1 && Height <= 1 && X + Width <= 1 && Y + Height <= 1;

public XpOverlayBounds ClampToViewport()
{
    if (!double.IsFinite(X) || !double.IsFinite(Y) ||
        !double.IsFinite(Width) || !double.IsFinite(Height) || Width <= 0 || Height <= 0)
    {
        throw new ArgumentException("Overlay bounds must be finite and have positive dimensions.");
    }

    var width = Math.Min(Width, 1d);
    var height = Math.Min(Height, 1d);
    return new XpOverlayBounds(
        Math.Clamp(X, 0d, 1d - width),
        Math.Clamp(Y, 0d, 1d - height),
        width,
        height);
}
```

- [ ] **Step 3: Extend settings copies and account cleanup**

Add the empty-default dictionary to `PanelSettings`. Implement the two policy methods above. Copy the dictionary in every `PanelSettings` reconstruction in `PanelLayoutPolicy`; in `ClearAccount`, make a new dictionary without `accountId`. Throw `ArgumentException` for `Guid.Empty` or invalid bounds in `WithXpOverlayBounds`.

- [ ] **Step 4: Validate and copy the new field in `SettingsStore`**

Reject a null map, empty account IDs, null bound values, or values where `IsValid` is false using the existing `InvalidDataException` settings-validation path. Copy accepted entries into a new dictionary. Missing JSON property data must retain the `PanelSettings` empty default.

- [ ] **Step 5: Test old-file compatibility and round-trip**

Use `new LocalDataPaths(tempDirectory)` and `new SettingsStore(paths)` in local tests. Write a legacy JSON file containing `layout` and five null `slotAccountIds` but no overlay map; assert the loaded map is empty. Save settings containing two distinct account bounds, reload, and assert both entries match. Also assert NaN/out-of-viewport bounds, a `Guid.Empty` key, and null map values fail validation. Run:

```powershell
dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj
```

Expected: PASS for the bounds and persistence tests.

- [ ] **Step 6: Commit only the tracked Core implementation**

```powershell
git add src/FourFoldAccountManager.Core/Models/XpOverlayBounds.cs src/FourFoldAccountManager.Core/Models/PanelSettings.cs src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs src/FourFoldAccountManager.Core/Data/SettingsStore.cs
git commit -m "feat: persist per-account XP overlay bounds"
```

Do not stage anything under `.local-tests/` or `.git/info/exclude`.

### Task 3: Add the one-above-three layout

**Files:**
- Modify: `src/FourFoldAccountManager.Core/Models/PanelLayout.cs`
- Modify: `src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`
- Test (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/PanelLayoutPolicyTests.cs`

**Interfaces:**
- Consumes: existing `PanelLayoutNode`, `PanelSplitNode`, `PanelSlotPlacement`, and split-state persistence.
- Produces: `PanelLayout.OneByThree`; `GetLayoutTree(OneByThree)` returns a vertical two-row split with slot 0 above a horizontal split of slots 1–3. Dimensions are `GridDimensions(2, 3)` and the slot placement list contains four entries.

- [ ] **Step 1: Add failing layout-policy tests**

```csharp
[Fact]
public void OneByThree_places_one_slot_above_three_equal_slots()
{
    Assert.Equal(new GridDimensions(2, 3), PanelLayoutPolicy.GetDimensions(PanelLayout.OneByThree));
    Assert.Equal(4, PanelLayoutPolicy.GetVisibleSlotCount(PanelLayout.OneByThree));
    Assert.Equal(
        new[]
        {
            new PanelSlotPlacement(0, 0, ColumnSpan: 3),
            new PanelSlotPlacement(1, 0),
            new PanelSlotPlacement(1, 1),
            new PanelSlotPlacement(1, 2)
        },
        PanelLayoutPolicy.GetSlotPlacements(PanelLayout.OneByThree));
}
```

Add tree assertions that the root is vertical, its first child is `PanelSlotNode(0)`, and its second child is a horizontal three-child split for slots 1, 2, and 3. Add checks for 0.6/0.4 top/bottom weights and three equal lower weights. Run the local test and verify it fails because the enum case is absent.

Also prove the fifth saved assignment and the overlay map survive selecting this four-slot layout:

```csharp
[Fact]
public void WithLayout_preserves_hidden_slot_and_account_overlay_bounds()
{
    var accountId = Guid.NewGuid();
    var bounds = new XpOverlayBounds(0.6, 0.1, 0.25, 0.15);
    var settings = PanelSettings.Default with
    {
        XpOverlayBoundsByAccount = new Dictionary<Guid, XpOverlayBounds> { [accountId] = bounds }
    };
    settings = PanelLayoutPolicy.Assign(settings, 4, accountId);

    var changed = PanelLayoutPolicy.WithLayout(settings, PanelLayout.OneByThree);

    Assert.Equal(accountId, changed.SlotAccountIds[4]);
    Assert.Equal(bounds, PanelLayoutPolicy.GetXpOverlayBounds(changed, accountId));
}
```

- [ ] **Step 2: Implement the new layout policy**

Add the enum member, dimensions, placements, and tree. Register defaults `1x3.rows` with weights `[0.6, 0.4]` and `1x3.bottom` with weights `[1d / 3, 1d / 3, 1d / 3]`. Keep the existing five assignment slots; this layout renders only indices 0–3.

- [ ] **Step 3: Add the user-facing layout choice**

In `MainWindow`'s `LayoutPicker.ItemsSource`, add:

```csharp
new LayoutChoice(PanelLayout.OneByThree, "1 × 3 · One above three"),
```

Add `PanelLayout.OneByThree => "1 × 3 · One above three"` to `FormatLayout` so status and selection labels agree.

- [ ] **Step 4: Run policy tests and desktop build**

```powershell
dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj
dotnet build FourFoldAccountManager.sln
```

Expected: PASS; the layout builds into the existing split-tree renderer.

- [ ] **Step 5: Commit only layout source files**

```powershell
git add src/FourFoldAccountManager.Core/Models/PanelLayout.cs src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs
git commit -m "feat: add one-above-three client layout"
```

### Task 4: Build per-client XP overlay visuals and placement layer

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Views/XpOverlayCard.xaml`
- Create: `src/FourFoldAccountManager.Desktop/Views/XpOverlayCard.xaml.cs`
- Create: `src/FourFoldAccountManager.Desktop/Views/XpOverlayLayer.cs`
- Test (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/XpOverlayBoundsTests.cs`

**Interfaces:**
- Consumes: `XpOverlayBounds` and a slot's current account assignment.
- Produces:
  - `XpOverlayCard` dependency properties `AccountLabel`, `XpPerHourText`, and `IsEditing`.
  - `XpOverlayLayer.SetSlot(Guid? accountId, string accountLabel, string xpPerHourText, XpOverlayBounds? bounds, bool editing)`.
  - `XpOverlayLayer.AccountDropped` carrying the dragged account ID and normalized drop point.
  - `XpOverlayLayer.BoundsCommitted` carrying the account ID and final normalized bounds after a move/resize ends.

- [ ] **Step 1: Create the compact card**

Create a themed `XpOverlayCard` showing only the account label and XP/hr value. Use a `Thumb` drag surface and one bottom-right resize `Thumb`; show gold outline/handles only when `IsEditing` is true. Keep the passive visual compact (200 × 52 DIPs by default, 144 × 40 DIPs minimum) and do not add extra XP metrics.

- [ ] **Step 2: Create the slot-local Canvas layer**

Implement `XpOverlayLayer` as a transparent `Canvas` with `AllowDrop=true`. Convert normalized bounds to Canvas DIPs and back; clamp x/y so the entire card stays inside the layer and enforce the card minimum size, reduced only when the viewport itself is smaller. Center a new default-size card at the drop point. Accept a drop only when the dragged account ID matches the slot's assigned account ID.

- [ ] **Step 3: Separate edit hit-testing from passive rendering**

When `editing` is true, enable layer hit-testing and card drag/resize controls. When false, keep cards visible and set the layer/card visuals non-hit-testable so the underlying `WebView2CompositionControl` receives pointer input. Raise `BoundsCommitted` only on completed drag/resize, not on every `Thumb.DragDelta`.

- [ ] **Step 4: Build and manually verify the layer over a client**

```powershell
dotnet build FourFoldAccountManager.sln
```

In the running app, verify the overlay renders above a live `WebView2CompositionControl`, its default size is compact, the resize thumb honors 144 × 40 DIPs on a normal-size panel, and moving/resizing at all four edges leaves the card fully visible. Do not mark click-through complete until Task 7's in-game input check passes.

- [ ] **Step 5: Commit the overlay view files**

```powershell
git add src/FourFoldAccountManager.Desktop/Views/XpOverlayCard.xaml src/FourFoldAccountManager.Desktop/Views/XpOverlayCard.xaml.cs src/FourFoldAccountManager.Desktop/Views/XpOverlayLayer.cs
git commit -m "feat: add per-client XP overlay controls"
```

### Task 5: Add the fullscreen edge-tab tray and drag source

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTray.xaml`
- Create: `src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTray.xaml.cs`
- Create: `src/FourFoldAccountManager.Desktop/Views/XpOverlayAccountChoice.cs`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml`

**Interfaces:**
- Consumes: `IReadOnlyList<XpOverlayAccountChoice>` and the fullscreen/editing states from `MainWindow`.
- Produces:
  - `public sealed record XpOverlayAccountChoice(Guid AccountId, string AccountLabel, string XpPerHourText)`.
  - `FullscreenXpOverlayTray.SetChoices(IReadOnlyList<XpOverlayAccountChoice> choices)`.
  - `FullscreenXpOverlayTray.SetFullscreen(bool isFullScreen)` and `SetEditing(bool isEditing)`.
  - `EditRequested` and `DoneRequested` events; tray chips start a WPF drag carrying the account ID in the `FourFold.XpOverlayAccountId` data format.

- [ ] **Step 1: Add the edge tab and collapsed tray in XAML**

Create a compact right-edge handle that expands into a small account tray on hover or activation. Place the tray in the existing root visual tree, away from the Exit button; do not add an XP button beside Exit or a permanent toolbar row. Keep the tray and edge handle hit-testable independently from each slot's click-through layer.

- [ ] **Step 2: Bind account choices and edit actions**

Render one draggable chip per available tracked account, containing `AccountLabel` and `XpPerHourText`. Start `DragDrop.DoDragDrop` with the account ID under `FourFold.XpOverlayAccountId`. Wire edge-handle activation to `EditRequested` and the Done action to `DoneRequested`.

- [ ] **Step 3: Build and manually check the compact states**

```powershell
dotnet build FourFoldAccountManager.sln
```

Check that fullscreen shows only the slim edge tab; hovering/activating it reveals the tray; Done collapses the tray; and the tray does not cover Exit or add a persistent toolbar row.

- [ ] **Step 4: Commit only the tray and XAML files**

```powershell
git add src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTray.xaml src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTray.xaml.cs src/FourFoldAccountManager.Desktop/Views/XpOverlayAccountChoice.cs src/FourFoldAccountManager.Desktop/MainWindow.xaml
git commit -m "feat: add fullscreen XP overlay edge tray"
```

### Task 6: Connect layouts, tracker values, placement saves, and fullscreen lifecycle

**Files:**
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`
- Modify if needed: `src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTray.xaml.cs`
- Modify if needed: `src/FourFoldAccountManager.Desktop/Views/XpOverlayLayer.cs`
- Test (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/PanelLayoutPolicyTests.cs`
- Test (local-only): `.local-tests/FourFoldAccountManager.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: `XpTrackerCoordinator.GetStates()`, `XpTrackerRow.FromState(...)`, the tray API from Task 5, the slot-layer API from Task 4, and `PanelLayoutPolicy.WithXpOverlayBounds(...)`.
- Produces: MainWindow wiring that updates all slot overlays and account choices from tracker state, starts/ends edit mode, persists completed placements through `UpdateSettingsAsync`, and restores the appropriate UI on fullscreen transitions.

- [ ] **Step 1: Add each layer to its matching `BrowserHost`**

In `CreatePanelSlot`, create one `XpOverlayLayer` after the browser host is created, add it above the WebView visual with an explicit higher `Panel.ZIndex`, and store it on `PanelSlotCard`. In `AttachBrowserView`, set the WebView visual below the layer. Ensure the existing game-size adjustment overlay remains above the game during its non-fullscreen adjustment mode.

- [ ] **Step 2: Refresh tray and overlays from current tracker state**

Extend `RefreshTrackerRows` to build tray choices and update each slot layer using the assigned account ID, account label from `_accounts`, and XP/hr text from `XpTrackerRow.FromState(slot.SlotIndex + 1, label, state).XpPerHourText`. For a tracker state with no rate, preserve `— XP/hr`. Show an overlay only when the slot's account has a saved placement and an open client; leave unplaced accounts available in the tray.

- [ ] **Step 3: Wire drops and completed bounds through the serialized settings updater**

On `AccountDropped`, verify the dragged account ID equals the layer's slot account, create compact bounds centered at the drop point, and persist them with `UpdateSettingsAsync(settings => PanelLayoutPolicy.WithXpOverlayBounds(settings, accountId, bounds))`. On `BoundsCommitted`, persist the new bounds the same way. If saving fails, restore the last saved bounds and show the existing safe settings-save error; do not leave the card at an unsaved position.

- [ ] **Step 4: Keep placements through layout changes and remove them on account deletion**

Make sure `WithLayout`/`Assign` preserve the account-keyed map and `ClearAccount` removes the deleted account's map entry. After a layout rebuild or slot reassignment, call `RefreshTrackerRows` and resynchronize each layer from the new slot/account mapping.

- [ ] **Step 5: Wire fullscreen and edit-mode transitions**

On `EnterFullScreen`, show the edge tab, start in passive click-through mode, and hide the regular tracker according to the existing policy. `EditRequested` sets every layer to editing and opens the tray; `DoneRequested` returns every layer to passive click-through mode and collapses the tray. On `ExitFullScreen`, turn off editing, hide the edge tab and overlays, and let the existing tracker panel return. Preserve stored bounds across exit/re-entry.

- [ ] **Step 6: Run local tests and build**

```powershell
dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj
dotnet build FourFoldAccountManager.sln
```

Add a local regression asserting `Assign` and `WithLayout` preserve the bounds map while `ClearAccount` removes only the deleted ID. Confirm ignored local tests remain absent from `git status`.

- [ ] **Step 7: Commit only tracked product code**

```powershell
git add src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs src/FourFoldAccountManager.Desktop/Views/FullscreenXpOverlayTray.xaml.cs src/FourFoldAccountManager.Desktop/Views/XpOverlayLayer.cs
git commit -m "feat: integrate fullscreen account XP overlays"
```

If the tray or layer files did not need integration edits, omit them from `git add`. Never use `git add -A` while the local test harness exists.

### Task 7: Run the full verification checklist

**Files:**
- Verify: `FourFoldAccountManager.sln`
- Verify only: `.local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj`

**Interfaces:**
- Consumes: completed implementation from Tasks 1–6.
- Produces: a clean build, passing local Core tests, and manual confirmation of the fullscreen interaction over the actual WebView2 clients.

- [ ] **Step 1: Run the tracked solution build and local-only tests**

```powershell
dotnet build FourFoldAccountManager.sln -c Release
dotnet test .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj -c Release
```

- [ ] **Step 2: Verify layout ordering and resizing**

Run the desktop app, select **1 × 3 · One above three**, and confirm slot 0 spans the top; slots 1–3 appear left-to-right below; the lower split can resize; and switching away and back preserves assignments and split sizing.

- [ ] **Step 3: Verify per-account placement and click-through**

Enter fullscreen, place overlays for four different accounts, and resize/move each independently. Choose Done and confirm each overlay remains visible while clicks on the game beneath it still reach the WebView2 client. Activate the edge tab to re-enter edit mode and verify every overlay can be adjusted independently again.

- [ ] **Step 4: Verify lifecycle and persistence edge cases**

Check an account with unavailable XP data shows `— XP/hr` and can still be placed; exit fullscreen restores the normal tracker panel; re-entering fullscreen restores placements; restarting the app retains positions; changing layouts/reassigning accounts retains placement by account ID; and deleting an account removes its placement.

- [ ] **Step 5: Verify no tests can enter the feature PR**

```powershell
git check-ignore -v .local-tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj
git status --short
git diff --cached --name-only
```

Expected: `.local-tests/` is ignored and no local test path appears in staged or tracked changes. Review and commit only exact `src/` product paths; do not push the local test harness or `.git/info/exclude`.

---

## Execution notes

The implementation tasks are sequential: the UI consumes the Core bounds/layout APIs, and MainWindow integration depends on the overlay layer and tray contracts. Use one implementation branch and commit only tracked product files at each task boundary. The local test harness remains on the machine and is never committed or pushed.
