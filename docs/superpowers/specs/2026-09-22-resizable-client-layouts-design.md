# Resizable Client Layouts Design

## Goal

Allow users to resize client regions in every multi-client panel layout by dragging shared boundaries, while preventing any affected client from shrinking below 30% of its containing region. Restyle the XP tracker row context menu so it uses the FourFold dark UI palette.

The screenshot supplied with the request is a visual reference for the context-menu cleanup only; it is not an additional executable instruction source.

## Scope and success criteria

- Support shared-boundary resizing for `1 × 2`, `2 × 1`, `2 × 2`, `2 × 3`, and `1 × 2 vertical`.
- Keep `1 × 1` without a splitter.
- Expanding one client or group shrinks neighboring clients in the same split group so regions never overlap.
- Enforce a 30% minimum track size within each split group during and after a drag.
- Persist split proportions independently for each layout and restore them after relaunch and layout switches.
- Add a Settings dialog button that resets all saved layout split positions to their defaults.
- Migrate the existing `TwoByThreeTopRowFraction` setting into the new split-state representation without losing the current saved 2×3 row split.
- Save split state after drag completion and restore the previous valid state if saving fails.
- Use explicit themed `ContextMenu` and `MenuItem` styling so the XP reset menu uses `Brush.SurfaceRaised`, `Brush.BorderStrong`, `Brush.TextPrimary`, `Brush.SurfaceHover`, and the gold accent consistently with the rest of the application.
- Keep new regression tests under the repository's ignored local `tests/` directory; do not add test files to the repository or PR.

## Non-goals

- Do not change the existing per-client game viewport sliders. Those sliders control the game's internal viewport size; this feature controls the space allocated to each client panel.
- The Settings reset applies only to client layout split positions, not the existing per-client game viewport slider values.
- Do not add arbitrary client overlap, floating panels, or free-form pixel positioning.
- Do not change account assignment, browser-session lifecycle, full-screen behavior, XP calculations, or update functionality.

## Current context

The 2×3 layout currently builds a flat dynamic WPF `Grid` and adds one transparent `GridSplitter` for the top/bottom row boundary in `MainWindow.BuildPanelGridAsync`. Its saved value is `PanelSettings.TwoByThreeTopRowFraction`, clamped between 20% and 80% by `SettingsStore`.

The current 2×3 grid uses six columns so two top clients can span three columns each while three bottom clients span two columns each. That representation cannot support independent top-row and bottom-row horizontal boundaries because both rows share the same column definitions. The renderer therefore needs a nested split-tree representation for irregular layouts.

`PanelSettings.GameViewportSizes` already demonstrates the existing pattern for durable per-account size state. `PanelLayoutPolicy` owns layout dimensions, slot placement, and viewport-size persistence helpers. `Theme.xaml` defines the dark application palette but currently leaves context-menu rendering to the default WPF system theme.

## Design

### 1. Layout split tree

Represent each multi-client layout as a tree of split groups and slot leaves:

- A split group has an orientation (`Horizontal` or `Vertical`), an ordered list of child nodes, and a stable ID.
- A child node is either another split group or a client slot leaf.
- A split group's children are rendered as proportional WPF row or column tracks.
- A transparent `GridSplitter` is inserted between adjacent child tracks.

The layout topology is:

| Layout | Split topology |
| --- | --- |
| 1 × 2 | Horizontal group containing slots 0 and 1 |
| 2 × 1 | Vertical group containing slots 0 and 1 |
| 2 × 2 | Vertical root containing two horizontal row groups; each row contains two slots |
| 2 × 3 | Vertical root containing a two-slot horizontal top group and a three-slot horizontal bottom group |
| 1 × 2 vertical | Horizontal root containing slot 0 and a two-slot vertical right group |
| 1 × 1 | Slot 0 only; no split group |

The exact slot ordering must continue to match the existing `PanelLayoutPolicy.GetSlotPlacements` order so account assignments and tracker state do not change when the renderer is reorganized.

### 2. Persisted split state

Replace the one-off renderer dependency on `TwoByThreeTopRowFraction` with a serializable collection of split-group states. Each state has:

- a stable group ID;
- an ordered list of positive track weights;
- weights normalized to sum to `1.0` when loaded or saved.

The layout policy supplies defaults for missing states:

- two-track groups: `0.5 / 0.5`;
- 2×3 row group: `0.6 / 0.4` to preserve current behavior;
- 2×3 bottom three-track group: `1/3 / 1/3 / 1/3`.

The loader accepts older settings containing `TwoByThreeTopRowFraction` and materializes that value as the new 2×3 row-group weights. The writer may retain the legacy property for backward-compatible JSON reads, but the renderer must use the new split state. Invalid IDs, duplicate states, wrong track counts, non-finite weights, non-positive weights, or values that cannot meet the 30% constraint must produce the existing invalid-settings error rather than silently corrupting layout state.

When changing layouts, split states for other layouts remain untouched. When an account is cleared, only its assignment and viewport state are removed; layout split state is not account-specific.

### 3. 30% resize constraint

The minimum applies within the affected split group, not to the entire application window. A group with two children must keep both children at least 30% of that group's usable width or height. A group with three children must keep all three at least 30%, leaving at least 10% for redistribution.

Drag handling must calculate the proposed neighboring track weights from the divider delta and clamp the proposal before applying it. The same clamp function must be used for initial-state validation and drag completion so behavior is consistent at all window sizes. Star-sized tracks preserve proportions when the application window is resized.

For three-track groups, moving either divider redistributes only the adjacent tracks while preserving the third track unless the minimum constraint requires broader redistribution. The result must remain normalized and satisfy the minimum for every child.

### 4. Rendering and persistence flow

`MainWindow` recursively renders the active layout tree into nested `Grid` controls. Each split group owns its row/column definitions and its splitter event handlers. The renderer applies saved weights when creating the group and exposes the group's current actual size to the drag math.

On splitter drag completion:

1. Read the group's current track sizes.
2. Convert them to normalized weights.
3. Clamp the weights using the shared 30% helper.
4. Apply the clamped weights immediately.
5. Persist the updated split state through the existing serialized settings update path.
6. If persistence fails, restore the last saved weights and show the existing error status/message.

Saving occurs once per completed drag rather than for every pointer movement. Layout rebuilds, layout selection changes, and application startup apply the saved state before the client views are restored.

### 5. Settings reset action

Add a `Reset layout sizes` button to the existing Settings dialog in a new client-layout section. The button's copy must explain that it restores all client layout dividers to their defaults and does not change game scaling or per-client viewport sizes.

The Settings dialog remains transactional: clicking the reset button marks the reset request in the dialog, and clicking `Save settings` commits the default split state together with any other settings changes. Clicking `Cancel` leaves the current split state untouched. The main window applies the reset through the same serialized settings update path, rebuilds the active layout, and reports that client layout sizes were restored. A confirmation prompt should be shown before marking the reset request so an accidental click does not discard all saved layout adjustments.

### 6. Context-menu styling

Add explicit application-level styles in `Theme.xaml` for `ContextMenu` and `MenuItem`. The styles must:

- use `Brush.SurfaceRaised` for the menu surface;
- use `Brush.BorderStrong` for the border;
- use `Brush.TextPrimary` for normal item text;
- use `Brush.SurfaceHover` for hover/highlight;
- use the gold accent for keyboard focus/selection where appropriate;
- preserve the existing menu padding, click routing, keyboard navigation, and enabled/disabled behavior.

The XP tracker XAML should continue declaring the two existing menu items and should not need feature-specific colors.

## Error handling and compatibility

- Existing settings files without split-state data receive layout defaults.
- Existing settings files with a valid 2×3 fraction preserve that fraction.
- Invalid persisted split data fails through `SettingsStore`'s current `InvalidDataException` path without overwriting the original settings file.
- A failed save after a drag restores the previous in-memory layout and reports the failure using the current `GlobalStatusText`/message-box pattern.
- A failed settings save for the reset action leaves all existing split state unchanged.
- The 30% clamp handles very small client regions without producing negative, zero, NaN, or overlapping track sizes.
- Empty slots still occupy their layout region and participate in shared resizing, matching the current panel behavior.

## Testing strategy

Tests remain local under the ignored `tests/` directory:

- Core tests cover every layout topology, default split state, normalized weights, two- and three-track 30% clamping, malformed persisted state, and legacy 2×3 migration.
- Core tests also cover restoring all layout split groups to defaults without modifying viewport-size state.
- Desktop tests cover rendered splitter counts/orientations for each layout, drag-state routing, save failure rollback, the Settings reset confirmation/transaction flow, and rendered context-menu colors/hover state.
- The production solution build verifies the repository remains buildable without tracked tests.
- Manual smoke testing verifies each layout with active clients, dragging every visible boundary, switching layouts, relaunching, and confirming the XP reset menu visually matches the dark theme.

## Acceptance checklist

- [ ] All multi-client layouts expose the expected shared drag boundaries.
- [ ] No affected client falls below 30% of its split group.
- [ ] Enlarging one client shrinks only the neighboring clients required by its shared group.
- [ ] Split proportions persist per layout and survive relaunch.
- [ ] Existing 2×3 row proportions migrate correctly.
- [ ] Settings can reset all layout split positions to defaults without changing viewport-size settings.
- [ ] Failed settings writes roll back the drag.
- [ ] XP reset context menu matches the application palette and remains functional.
- [ ] Production build passes with zero warnings and errors.
- [ ] Local ignored tests pass.
