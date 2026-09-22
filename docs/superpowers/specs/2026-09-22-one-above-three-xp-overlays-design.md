# One-Above-Three Layout and Fullscreen XP Overlays

**Status:** Design approved; awaiting written-spec review before implementation planning.

## Goal

Add a four-client layout with one full-width client above three equal clients, and let users place a compact per-account XP-per-hour overlay over each client while playing fullscreen. Each account's overlay can be moved and resized independently. In normal play the overlays must not intercept game input, and the controls must use very little screen space.

## Agreed experience

- The new layout is presented as **1 × 3 · One above three**: slot 0 occupies the top row; slots 1, 2, and 3 occupy the lower row from left to right. This is four visible clients. Existing assignments remain tied to their slot indices; the fifth slot, used by other layouts, is not shown here.
- Fullscreen has a compact auto-hide tab at the screen edge, not an XP button beside Exit. Hovering or activating the tab opens a small account tray; the tab remains reachable after overlays become click-through.
- In a single overlay-editing mode, the user drags an account's XP/hr overlay from the tray onto that account's client. Placed overlays can each be moved and resized separately. **Done** exits editing and makes the overlays non-interactive so pointer input reaches the game beneath them. Activating the edge tab re-enters editing.
- The overlay displays the account label and XP/hr value. If a rate is not currently available, it displays the existing unavailable-value convention (`— XP/hr`). The standard XP tracker panel remains the non-fullscreen experience; these floating overlays are fullscreen-only.

## Architecture

### Layout policy

Add a `PanelLayout.OneByThree` value and a layout-picker choice. Extend `PanelLayoutPolicy` with a two-row, three-column topology: a vertical split places slot 0 above a horizontal three-way split for slots 1–3. The default top/bottom split is 60/40, and the lower row is divided equally. Add the corresponding dimensions, placements, visible-slot count, and persisted split-state identifiers. The layout uses the existing split sizing and persistence mechanisms rather than introducing a second layout engine.

### XP overlay host and data

Keep overlays in the WPF visual tree, layered within each slot's existing `BrowserHost` above its `WebView2CompositionControl`. This keeps each overlay attached to its client as the split layout changes and avoids separate topmost windows, screen-coordinate tracking, and OS z-order management.

Use the existing XP tracker as the source of per-account values. A compact overlay view presents only the account label and XP/hr. A fullscreen edge-tab/tray control exposes the tracked accounts and starts the placement/editing interaction. In edit mode, overlays are hit-testable and show drag/resize affordances. In play mode, the overlay visuals are not hit-testable; the edge tab remains interactive so users can return to edit mode.

### Independent placement and persistence

Persist overlay bounds keyed by account ID in `PanelSettings`, alongside the existing per-account viewport settings. Store bounds as normalized fractions of the owning client viewport (`x`, `y`, `width`, `height`) so they scale with the client and follow the account if the account is assigned to another slot. Each account retains its own size and position. A new placement starts at the drop location with a compact default size; movement and resizing are constrained to the client viewport and enforce a usable minimum size.

Add the new optional settings property with an empty default so existing settings files load unchanged. Extend `SettingsStore` validation/copying and layout/settings transformations to preserve valid overlay placements. Removing an account removes its placement; changing layouts or temporarily hiding a slot does not discard it. Save the resulting bounds when a drag or resize completes, not on every pointer movement.

## Interaction and lifecycle

1. Enter fullscreen; the edge tab is available without adding a permanent toolbar row.
2. Open the tab to reveal the compact account tray and enter edit mode.
3. Drag an account overlay onto its client. Existing overlays show their handles and can be independently moved or resized during the same edit session.
4. Choose **Done**. Handles and tray close, and every placed overlay becomes click-through while remaining visible.
5. Use the edge tab to edit placements again. Exiting fullscreen hides the overlays and restores the regular tracker panel; saved placements remain for the next fullscreen session.

An unavailable XP rate is rendered as `— XP/hr` without preventing placement. Dropped and resized bounds are constrained to the client so an overlay cannot be lost outside its viewport. Invalid persisted bounds follow the existing settings-validation error path rather than being used unchecked.

## Verification

The repository currently has no test project. Add a small **local-only** Core test project under an ignored local path and a repository-local `.git/info/exclude` rule. Run it locally, but do not add the test project, its source, or its dependencies to tracked changes, commits, or the feature PR.

The local tests cover the new layout tree/placements and visible-slot count; default and persisted split weights; overlay-bound validation and clamping; old settings files with no overlay property; round-tripping independent account bounds; and placement cleanup when an account is removed. The normal tracked solution build remains part of verification.

Manual desktop verification covers the complete flow with embedded clients: 1×3 ordering and resizing, tray drag/drop, independent overlay placement and resizing, **Done** click-through reaching the WebView2 game, reopening edit mode from the edge tab, fullscreen exit/restoration of the normal tracker, and placement persistence after restart. The WebView2 composition layering and click-through behavior must be verified in the running app.

## Out of scope

- Separate native/topmost overlay windows or overlays outside the FourFold window.
- Floating XP overlays outside fullscreen.
- Additional tracker metrics beyond account label and XP/hr.
- Shipping the local-only test project in the repository or PR.

## Acceptance criteria

- Users can choose the new one-above-three layout and see four clients in the specified order.
- Fullscreen overlay editing is reachable from the compact edge tab without an XP button beside Exit.
- Each account overlay can be independently positioned and resized, and its placement survives layout resizing and app restart.
- After **Done**, overlay visuals remain visible but do not intercept clicks intended for the game; users can reopen editing from the edge tab.
- Existing settings files and non-fullscreen tracker behavior continue to work.
- Local-only tests run successfully and no test files or test dependencies appear in tracked changes or the PR.
