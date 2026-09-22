# Resizable Client Layouts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add shared-boundary client resizing with a 30% minimum to every multi-client layout, add a Settings reset-to-defaults action, and restyle the XP tracker context menu to match the dark FourFold UI.

**Architecture:** Replace the one-off 2×3 row splitter with a Core-owned nested layout tree whose split groups store normalized track weights. The WPF desktop renderer recursively creates nested grids and shared GridSplitter boundaries, persists completed drag changes through PanelSettings, and resets all split groups through the existing Settings dialog save flow. Explicit ContextMenu and MenuItem styles will consume existing Theme resources.

**Tech Stack:** .NET 10, C# records, System.Text.Json, WPF Grid/GridSplitter, xUnit local test projects, existing FourFold settings and UI resources.

**Spec:** docs/superpowers/specs/2026-09-22-resizable-client-layouts-design.md

## Global Constraints

- Preserve existing PanelLayoutPolicy.GetSlotPlacements slot ordering and account assignment behavior.
- Support 1 × 2, 2 × 1, 2 × 2, 2 × 3, and 1 × 2 vertical; 1 × 1 has no splitter.
- Keep every affected split-group child at least 30% of that group's usable width or height.
- Store normalized positive track weights that sum to 1.0; reject NaN, infinity, zero, negative, duplicate, or unknown split state.
- Migrate valid legacy TwoByThreeTopRowFraction values into the new 2×3 row split state.
- The Settings reset changes layout split state only; it must not change GameViewportSizes, game scaling, account assignments, or full-screen settings.
- Save splitter changes on drag completion and restore the prior valid state when persistence fails.
- New tests live under the ignored local tests directory and must not be staged or committed.
- The production solution must build without test-project references.
- XP context-menu styles must use existing Brush.SurfaceRaised, Brush.BorderStrong, Brush.TextPrimary, Brush.SurfaceHover, and gold-accent resources.

## Review Focus

- Three-track redistribution: dragging either 2×3 bottom-row divider must preserve the unaffected track unless the 30% floor requires redistribution.
- Irregular topology: 2×3 top and bottom rows must resize independently while the row divider resizes both nested groups together.
- Settings compatibility: legacy 2×3 state, missing state, malformed IDs, wrong track counts, and invalid weights must be deterministic.
- Reset transaction and rollback: confirmation, Cancel, Save, and failed persistence must leave viewport-size data and prior split state correct.
- WPF styling: rendered context-menu colors and click routing must use the FourFold palette rather than the white system default.

---

### Task 1: Add local Core test harness and split-state math

**Files:**

- Create locally, ignored: tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj
- Create locally, ignored: tests/FourFoldAccountManager.Core.Tests/LayoutSplitTests.cs
- Create: src/FourFoldAccountManager.Core/Models/PanelSplitState.cs
- Create: src/FourFoldAccountManager.Core/Panel/PanelSplitMath.cs

**Interfaces:**

- PanelSplitState(string Id, IReadOnlyList<double> Weights)
- PanelSplitOrientation with Horizontal and Vertical values
- PanelSplitMath.Normalize(IReadOnlyList<double>)
- PanelSplitMath.ClampToMinimum(IReadOnlyList<double>, double minimum = 0.30)
- PanelSplitMath.AdjustBoundary(IReadOnlyList<double>, int boundaryIndex, double deltaFraction, double minimum = 0.30)

- [ ] Step 1: Create the ignored local Core test project.

Target net10.0, mark IsTestProject true, reference Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio, and the Core project. Do not add it to FourFoldAccountManager.sln.

~~~
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\FourFoldAccountManager.Core\FourFoldAccountManager.Core.csproj" />
  </ItemGroup>
</Project>
~~~

- [ ] Step 2: Write failing normalization and clamp tests.

Use these exact expectations:

~~~
[Fact]
public void Normalize_returns_equal_weights_for_equal_sizes()
{
    var result = PanelSplitMath.Normalize(new[] { 2d, 2d });
    Assert.Equal(new[] { 0.5d, 0.5d }, result);
}

[Fact]
public void ClampToMinimum_redistributes_three_tracks()
{
    var result = PanelSplitMath.ClampToMinimum(new[] { 0.1d, 0.8d, 0.1d });
    Assert.Equal(new[] { 0.3d, 0.4d, 0.3d }, result);
}

[Fact]
public void AdjustBoundary_changes_only_adjacent_tracks_when_possible()
{
    var result = PanelSplitMath.AdjustBoundary(new[] { 0.3d, 0.4d, 0.3d }, 0, 0.1d);
    Assert.Equal(new[] { 0.4d, 0.3d, 0.3d }, result);
}
~~~

Also test invalid values including NaN, infinity, zero, negative weights, null input, fewer than two tracks, and invalid boundary indexes.

- [ ] Step 3: Run the focused test and verify it fails.

~~~
dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj -c Release --nologo --filter FullyQualifiedName~LayoutSplitTests
~~~

Expected: compile failure because PanelSplitMath and PanelSplitState do not exist.

- [ ] Step 4: Implement the immutable records and math.

Normalize must reject invalid input and return read-only normalized values. ClampToMinimum must reserve the minimum for every track, redistribute the remaining weight proportionally, and reject a minimum outside (0, 1 / trackCount]. AdjustBoundary must update the adjacent tracks and then use the same clamp helper.

~~~
public enum PanelSplitOrientation
{
    Horizontal,
    Vertical
}

public sealed record PanelSplitState(string Id, IReadOnlyList<double> Weights);
~~~

- [ ] Step 5: Run the focused Core tests and verify they pass.

- [ ] Step 6: Commit only production Core files.

~~~
git add src/FourFoldAccountManager.Core/Models/PanelSplitState.cs src/FourFoldAccountManager.Core/Panel/PanelSplitMath.cs
git commit -m "feat: add shared layout split math"
~~~

Leave local tests ignored and uncommitted.

### Task 2: Define layout topology and persisted split state

**Files:**

- Create: src/FourFoldAccountManager.Core/Panel/PanelLayoutNode.cs
- Modify: src/FourFoldAccountManager.Core/Models/PanelSettings.cs
- Modify: src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs
- Modify: src/FourFoldAccountManager.Core/Data/SettingsStore.cs
- Modify locally, ignored: tests/FourFoldAccountManager.Core.Tests/LayoutSplitTests.cs

**Interfaces:**

- PanelLayoutNode, PanelSlotNode(int SlotIndex), and PanelSplitNode(string Id, PanelSplitOrientation Orientation, IReadOnlyList<PanelLayoutNode> Children)
- PanelLayoutPolicy.GetLayoutTree(PanelLayout)
- PanelLayoutPolicy.GetDefaultSplitStates()
- PanelLayoutPolicy.GetSplitState(PanelSettings, string)
- PanelLayoutPolicy.WithSplitState(PanelSettings, PanelSplitState)
- PanelLayoutPolicy.ResetSplitStates(PanelSettings)
- PanelSettings.SplitStates while retaining TwoByThreeTopRowFraction for migration

- [ ] Step 1: Write failing topology, defaults, reset, and migration tests.

Test that the 2×3 root is a vertical split with independent 2×3 top and 2×3 bottom groups. Test every layout's slot leaves against the existing placement order. Test ResetSplitStates restores defaults while preserving GameViewportSizes, account assignments, scaling, and full-screen settings. Test a legacy JSON settings object with TwoByThreeTopRowFraction 0.7 produces 2×3 row weights [0.7, 0.3].

~~~
[Fact]
public void ResetSplitStates_preserves_viewport_sizes()
{
    var accountId = Guid.NewGuid();
    var settings = PanelSettings.Default with
    {
        GameViewportSizes = new Dictionary<Guid, GameViewportSize>
        {
            [accountId] = new(72, 84)
        },
        SplitStates = new[]
        {
            new PanelSplitState("2x3.rows", new[] { 0.3d, 0.7d })
        }
    };

    var reset = PanelLayoutPolicy.ResetSplitStates(settings);

    Assert.Equal(settings.GameViewportSizes, reset.GameViewportSizes);
    Assert.Equal(new[] { 0.6d, 0.4d },
        PanelLayoutPolicy.GetSplitState(reset, "2x3.rows").Weights);
}
~~~

Also test duplicate/unknown IDs, wrong track counts, invalid weights, and absent split state defaults.

- [ ] Step 2: Run focused Core tests and verify they fail.

~~~
dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj -c Release --nologo --filter FullyQualifiedName~LayoutSplitTests
~~~

Expected: compile failures for the new topology and settings APIs.

- [ ] Step 3: Implement the layout tree.

Use these topologies:

~~~
1 × 2:           horizontal(1x2.columns, slot 0, slot 1)
2 × 1:           vertical(2x1.rows, slot 0, slot 1)
2 × 2:           vertical(2x2.rows, horizontal(2x2.top, 0, 1), horizontal(2x2.bottom, 2, 3))
2 × 3:           vertical(2x3.rows, horizontal(2x3.top, 0, 1), horizontal(2x3.bottom, 2, 3, 4))
1 × 2 vertical: horizontal(1x2v.columns, slot 0, vertical(1x2v.right.rows, 1, 2))
1 × 1:           slot 0
~~~

Keep GetSlotPlacements available for account assignment and tracker ordering; the new tree is the rendering topology.

- [ ] Step 4: Add split state to PanelSettings and preserve it through policy updates.

Use these default IDs and weights:

~~~
1x2.columns      [0.5, 0.5]
2x1.rows         [0.5, 0.5]
2x2.rows         [0.5, 0.5]
2x2.top          [0.5, 0.5]
2x2.bottom       [0.5, 0.5]
2x3.rows         [0.6, 0.4]
2x3.top          [0.5, 0.5]
2x3.bottom       [1/3, 1/3, 1/3]
1x2v.columns     [0.5, 0.5]
1x2v.right.rows  [0.5, 0.5]
~~~

ResetSplitStates replaces all split states with defaults and preserves every unrelated PanelSettings property.

- [ ] Step 5: Add validation and legacy migration to SettingsStore.

When split state is absent, materialize defaults. When 2x3.rows is absent but TwoByThreeTopRowFraction is valid, materialize that legacy fraction. Validate every ID, track count, weight, and minimum constraint using the same Core math. Return a new PanelSettings with normalized states and existing viewport/account/full-screen values.

- [ ] Step 6: Run all local Core tests and verify they pass.

~~~
dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj -c Release --nologo
~~~

- [ ] Step 7: Commit only production Core files.

~~~
git add src/FourFoldAccountManager.Core/Models/PanelSplitState.cs src/FourFoldAccountManager.Core/Panel/PanelSplitMath.cs src/FourFoldAccountManager.Core/Panel/PanelLayoutNode.cs src/FourFoldAccountManager.Core/Models/PanelSettings.cs src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs src/FourFoldAccountManager.Core/Data/SettingsStore.cs
git commit -m "feat: model resizable panel layouts"
~~~

### Task 3: Render nested layouts and enforce shared drag resizing

**Files:**

- Modify: src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs
- Modify locally, ignored: tests/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj
- Create locally, ignored: tests/FourFoldAccountManager.Desktop.Tests/LayoutResizeRenderingTests.cs

**Interfaces:**

- Consume PanelLayoutPolicy.GetLayoutTree, GetSplitState, PanelSplitMath.ClampToMinimum, and PanelSplitMath.AdjustBoundary.
- Replace the special-case 2×3 block in RebuildPanelAsync with recursive rendering.
- Produce one rendered split group and one invisible GridSplitter per adjacent child pair.

- [ ] Step 1: Create the ignored local Desktop test project.

Target net10.0-windows10.0.17763.0, set UseWPF=true and IsTestProject=true, reference the same xUnit packages, the Desktop project, and the Core project. Keep it outside the solution and ignored.

- [ ] Step 2: Write failing renderer tests.

Use STA tests that render every layout through an internal test seam and assert splitter counts:

~~~
1 × 2             1 splitter
2 × 1             1 splitter
2 × 2             3 splitters
2 × 3             4 splitters
1 × 2 vertical    2 splitters
1 × 1             0 splitters
~~~

Also assert that 2×3 top and bottom horizontal splitters are independent and that a completed drag stores clamped normalized weights.

- [ ] Step 3: Run the focused Desktop test and verify it fails.

~~~
dotnet test tests/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj -c Release --nologo --filter FullyQualifiedName~LayoutResizeRenderingTests
~~~

Expected: compile failure because recursive rendering and the test seam do not exist.

- [ ] Step 4: Replace the flat grid build with recursive nested-grid rendering.

Refactor RebuildPanelAsync to obtain PanelLayoutPolicy.GetLayoutTree(_panelSettings.Layout), build a root element recursively, and add it to PanelGridHost. Keep PanelSlotCard bookkeeping by having each PanelSlotNode call CreatePanelSlot(slotIndex) in existing slot order.

~~~
private FrameworkElement BuildLayoutNode(PanelLayoutNode node)
{
    return node switch
    {
        PanelSlotNode slot => BuildSlotElement(slot.SlotIndex),
        PanelSplitNode split => BuildSplitElement(split),
        _ => throw new ArgumentOutOfRangeException(nameof(node))
    };
}
~~~

BuildSplitElement must create row definitions for Vertical groups, column definitions for Horizontal groups, apply saved weights as star lengths, recursively add children, and insert transparent GridSplitters between adjacent children. Do not use the old six-column 2×3 span trick.

- [ ] Step 5: Enforce the 30% minimum during drag and persist on completion.

Set affected row/column definition minimums to 30% of the current group dimension. Use GridSplitter drag events to apply adjacent changes through PanelSplitMath. On DragCompleted, normalize current track sizes and persist:

~~~
await UpdateSettingsAsync(currentSettings =>
    PanelLayoutPolicy.WithSplitState(
        currentSettings,
        new PanelSplitState(split.Id, weights)));
~~~

If persistence throws, restore the prior group weights and show the existing layout-save error message. Save once per completed drag.

- [ ] Step 6: Add deterministic Desktop coverage.

Expose only an internal renderer/test seam. Tests must not launch live account or browser services. Cover 2×3 row resizing, independent top/bottom boundaries, three-track clamping, applying saved weights on rebuild, and save-failure rollback.

- [ ] Step 7: Run local Desktop tests and the production build.

~~~
dotnet test tests/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj -c Release --nologo
dotnet build FourFoldAccountManager.sln -c Release --nologo
~~~

- [ ] Step 8: Commit only production renderer files.

~~~
git add src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs src/FourFoldAccountManager.Desktop/Properties/AssemblyInfo.cs
git commit -m "feat: resize client layout boundaries"
~~~

### Task 4: Add the Settings reset-to-defaults action

**Files:**

- Modify: src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml
- Modify: src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml.cs
- Modify: src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs
- Create locally, ignored: tests/FourFoldAccountManager.Desktop.Tests/SettingsDialogTests.cs
- Modify locally, ignored: tests/FourFoldAccountManager.Core.Tests/LayoutSplitTests.cs

**Interfaces:**

- SettingsDialog exposes bool ResetLayoutSizes { get; }.
- PanelLayoutPolicy.ResetSplitStates is the only source of reset defaults.
- MainWindow.Settings_Click applies the reset only after the dialog returns true from Save.

- [ ] Step 1: Add Core reset coverage.

Assert ResetSplitStates restores every supported layout group, preserves GameViewportSizes, FillGameToPanel, ShowFullScreenExitButton, and SlotAccountIds, and does not mutate the original settings object.

- [ ] Step 2: Add failing Settings dialog interaction tests.

Assert the dialog renders a Reset layout sizes button, confirmation Yes sets ResetLayoutSizes=true, No leaves it false, and Cancel does not commit a reset.

- [ ] Step 3: Implement the Settings dialog UI and transaction flag.

Add a client-layout section with explanatory copy and a themed secondary button. The click handler confirms with the user, sets the flag only for MessageBoxResult.Yes, and Save_Click returns true as it does for existing settings.

~~~
<TextBlock Text="CLIENT LAYOUT" Foreground="{DynamicResource Brush.AccentGold}" />
<Button Content="Reset layout sizes"
        Click="ResetLayoutSizes_Click"
        Style="{StaticResource AppButtonStyle}" />
~~~

- [ ] Step 4: Apply reset through MainWindow.Settings_Click.

When the dialog saves, pass current settings through ResetSplitStates when ResetLayoutSizes is true, while separately applying existing scaling/full-screen values. Persist once, rebuild the panel without closing views, and report success. On failure keep old _panelSettings and show the existing settings error.

- [ ] Step 5: Run focused reset tests.

~~~
dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj -c Release --nologo --filter FullyQualifiedName~ResetSplitStates
dotnet test tests/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj -c Release --nologo --filter FullyQualifiedName~SettingsDialogTests
~~~

- [ ] Step 6: Commit only production Settings files.

~~~
git add src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml src/FourFoldAccountManager.Desktop/Views/SettingsDialog.xaml.cs src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs
git commit -m "feat: reset client layout sizes from settings"
~~~

### Task 5: Match the XP context menu to the FourFold theme

**Files:**

- Modify: src/FourFoldAccountManager.Desktop/Resources/Theme.xaml
- Create locally, ignored: tests/FourFoldAccountManager.Desktop.Tests/ContextMenuThemeTests.cs

**Interfaces:**

- Existing XpTrackerPanel.xaml menu headers and click handlers remain unchanged.
- Produces application-level ContextMenu and MenuItem styles.

- [ ] Step 1: Write failing rendered-style tests.

Render the XP tracker menu through the application resource dictionary and assert SurfaceRaised background, BorderStrong border, TextPrimary normal text, SurfaceHover highlight, gold focus/selection, and unchanged Reset action routing.

- [ ] Step 2: Run the focused test and verify it fails.

~~~
dotnet test tests/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj -c Release --nologo --filter FullyQualifiedName~ContextMenuThemeTests
~~~

Expected: the default WPF context-menu template exposes the light system palette.

- [ ] Step 3: Add explicit dark ContextMenu and MenuItem styles.

Set the existing palette brushes and provide template/trigger behavior for normal, hover, pressed, keyboard-focused, and disabled states. Preserve padding and keyboard navigation; do not use hardcoded white/black system colors.

- [ ] Step 4: Run the local Desktop suite and production build.

~~~
dotnet test tests/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj -c Release --nologo
dotnet build FourFoldAccountManager.sln -c Release --nologo
~~~

- [ ] Step 5: Commit the theme change.

~~~
git add src/FourFoldAccountManager.Desktop/Resources/Theme.xaml
git commit -m "fix: theme xp tracker context menu"
~~~

### Task 6: Document behavior and complete verification

**Files:**

- Modify: README.md
- Modify locally, ignored: tests/FourFoldAccountManager.Core.Tests/LayoutSplitTests.cs
- Modify locally, ignored: tests/FourFoldAccountManager.Desktop.Tests/LayoutResizeRenderingTests.cs

- [ ] Step 1: Update README.

Document shared drag boundaries for every multi-client layout, the 30% minimum, per-layout persistence, Settings → Reset layout sizes, the fact that viewport sizes are unchanged by the reset, and that 1 × 1 has no boundaries.

- [ ] Step 2: Run complete local verification.

~~~
dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj -c Release --nologo
dotnet test tests/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj -c Release --nologo
dotnet build FourFoldAccountManager.sln -c Release --nologo
git diff --check origin/main..HEAD
~~~

Expected: local tests pass, production build has 0 warnings and 0 errors, and the diff check is clean.

- [ ] Step 3: Perform manual UI smoke testing.

Launch src/FourFoldAccountManager.Desktop/bin/Release/net10.0-windows10.0.17763.0/FourFoldAccountManager.Desktop.exe with active clients. Verify each supported layout, drag every boundary in both directions, confirm no affected client falls below 30%, switch layouts, relaunch, use Settings → Reset layout sizes, and right-click the XP tracker row to confirm the dark menu palette and Reset actions.

- [ ] Step 4: Commit documentation.

~~~
git add README.md
git commit -m "docs: describe resizable client layouts"
~~~
