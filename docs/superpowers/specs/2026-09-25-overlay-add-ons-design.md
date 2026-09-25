# Full-screen overlay add-ons design

**Status:** Design approved in conversation on 2026-09-25; awaiting written-spec review.

## Context and goal

The user wants a stopwatch **Timer** plugin for speedrunners. They also want the full-screen overlay to show each plugin as an optional add-on card, not only XP/hr. The work is split into three projects, each with its own spec, plan, and PR:

1. **Overlay add-on system (this spec).** Generalize the full-screen overlay into a host for plugin cards with on/off switches, and move the existing XP/hr card into it.
2. **Timer plugin.** Sidebar tab, split/finish/reset global hotkeys, lap list, and a global Timer overlay card.
3. **Stats and XP calc overlay cards.** Compact per-account cards for the remaining plugins.

The goal of this project is that adding a later card needs only a data record, a template, and one catalog entry. The user-visible result is the same XP/hr card, now switched on and off from an **Overlays** panel instead of being dragged onto a client.

## Current behavior

- In full screen, every client slot has an `XpOverlayLayer` canvas. It holds at most one `XpOverlayCard` for that slot's account.
- The edge-tab tray, `FullscreenXpOverlayTray`, lists open accounts. Dragging an account onto its own client places that account's XP card.
- Card positions are stored as normalized `XpOverlayBounds` in `PanelSettings.XpOverlayBoundsByAccount`.
- The layers are hit-test visible only in edit mode, so the game keeps mouse input.
- Edit mode opens from the edge tab or the reveal hotkey (Ctrl+Alt+Shift+O) and closes with **Done**.

## Design

### 1. Data model and settings (Core)

- **Add-on catalog.** A static catalog in Core defines each `OverlayAddOnKind`. For each kind it records:
  - its scope: `Account` or `Global`;
  - its display name;
  - its default size in pixels;
  - its minimum size in pixels.

  This project registers only `Xp` (Account scope). Later projects add `Timer` (Global), `Stats`, and `XpCalc` (Account) with one entry each. The Overlays panel shows switches only for registered kinds.
- **Placement.** `OverlayCardPlacement(Kind, AccountId?, Enabled, Bounds?)` stores one card.
  - `AccountId` is required for Account scope and must be null for Global scope.
  - `Bounds` are normalized to the card's layer: the client for Account scope, the full-screen window for Global scope.
  - `Enabled` is independent of `Bounds`, so turning a card off and on keeps its position.
- **Rename.** `XpOverlayBounds` is renamed `OverlayBounds`. This is a type rename only; JSON property names are unchanged.
- **Settings.** `PanelSettings.OverlayCards` (a list of placements) replaces `XpOverlayBoundsByAccount`. The old property can still be read, but new settings no longer write it.
- **Migration.** When loading settings that have no `OverlayCards` but do have `XpOverlayBoundsByAccount` entries, each old entry becomes an enabled `Xp` placement at the same bounds. Existing users see no change.
- **Validation.** `SettingsStore.Validate` removes a placement that has:
  - an unknown kind;
  - a scope mismatch;
  - a duplicate key (kind plus account). The first entry is kept.

  Invalid bounds become null. Every place that rebuilds `PanelSettings` field by field must copy `OverlayCards`, or the value is silently lost. Today that is `SettingsStore.Validate` and the rebuilds in `PanelLayoutPolicy`.
- **Account removal.** This removes the account's placements, replacing the current XP-bounds cleanup in `PanelLayoutPolicy`.

### 2. Layers and card frame (Desktop)

- **`OverlayCardFrame`** is a `ContentControl` that holds the shared chrome taken from `XpOverlayCard`: border, move thumb, resize grip, and edit styling. Each add-on provides a data record. A `DataTemplate` keyed by that record's type renders the card content.
- **`OverlayCardLayer`** replaces `XpOverlayLayer`. It is a canvas that holds any number of frames.
  - `SetCards(IReadOnlyList<OverlayCardModel> cards, bool editing)` adds, updates, and removes frames by key (kind plus account). Each model supplies bounds, minimum size, and data.
  - The existing move, resize, and clamp logic is reused for each card.
  - `BoundsCommitted(key, bounds)` fires when a move or resize completes. A cancelled drag restores the saved bounds.
- **Two layers in full screen:**
  - One layer per client slot, in the same position and z-order as today's layer, for Account-scoped cards.
  - One new window-wide layer above the client grid and below the edge-tab tray, for Global cards.
  - Both layers exist only in full screen.
- **Input.** Layers are hit-test invisible outside edit mode, so clicks and keys reach the game.
- **Default position.** A card with null bounds is drawn at a default position once its layer has a size:
  - Account cards go at the top-left of their client.
  - Global cards go at the top-centre of the window.
  - Each additional enabled card in the same layer is offset by a small cascade step.
  - The default is not saved until the user moves or resizes the card. This calculation lives in a Core helper so it can be tested without WPF.
- **Z-order.** Cards follow catalog order. The card being manipulated comes to the front.
- **XP content.** The XP content is a `DataTemplate` that shows the account label and XP/hr, visually identical to today's card.

### 3. Overlays panel and user flow

- `FullscreenXpOverlayTray` becomes `FullscreenOverlayTray`, headed "Overlays" with the hint "Switch cards on, then drag to place them".
- **Panel sections:**
  - A **Global** section, shown only when a Global kind is registered. None exist in this project.
  - One row per open account in a slot, showing the account label and a switch for each registered Account-scoped kind. In this project that is **XP/hr**, with the current XP/hr value beside it.
  - Accounts that are not open are not listed, and their placements are kept.
- **Switching a card on** sets `Enabled` and saves settings. The card appears at its saved bounds, or at the default position if none are saved, and can be dragged immediately because the panel is in edit mode.
- **Switching a card off** clears `Enabled` and keeps its bounds.
- **Opening and closing edit mode** is unchanged: edge tab hover or click, or the reveal hotkey, then **Done**. The Settings label becomes "Reveal overlays tab". The persisted property `RevealXpOverlayTabShortcut` keeps its name for compatibility.
- **Removed:** the drag-an-account-onto-its-client placement, `XpOverlayLayer.AccountDragDataFormat`, and the drop handling.
- **Defaults:** new accounts have the XP card switched off. Migrated users keep their placed XP cards switched on.
- **Saving:** all changes are saved through the existing `UpdateSettingsAsync` path.

## Error handling and edge cases

- **A card is enabled before its layer is measured.** The card is saved as enabled with null bounds and drawn at the default position after measurement.
- **The window or a divider is resized.** Normalized bounds clamp to the layer, as today.
- **An account moves to another slot.** Account-scoped cards follow the account, not the slot.
- **The user leaves full screen during a drag.** The manipulation is cancelled and the saved bounds are restored.
- **A settings save fails.** The switch reverts to its previous state and the existing status message is shown, so the UI never disagrees with persisted settings.
- **Settings are corrupt or hand-edited.** They are normalized by validation, never throw, and never crash the app.

## Testing

- **Core (xUnit):**
  - catalog scope rules;
  - migration from `XpOverlayBoundsByAccount` to enabled XP placements;
  - validation of unknown kinds, scope mismatches, duplicates, and invalid bounds;
  - a save/load round trip that keeps `OverlayCards` through `Validate` and the `PanelLayoutPolicy` rebuilds;
  - account removal removing placements;
  - default and cascade placement maths.
- **Desktop (STA xUnit):**
  - `OverlayCardLayer` adds, updates, and removes frames by key;
  - the layer clamps bounds;
  - `BoundsCommitted` reports the right key;
  - the layer is hit-test invisible outside edit mode;
  - `FullscreenOverlayTray` lists open accounts and raises switch events.
- **Manual check:**
  1. Enter full screen with two or more clients.
  2. Switch XP on for each client, then move and resize the cards.
  3. Switch a card off and on again, and confirm it returns to the same place.
  4. Restart the app and confirm the cards are unchanged.
  5. Upgrade from a settings file that already has placed XP cards, and confirm they appear unchanged.

## Out of scope

- Timer, Stats, and XP calc card content.
- New hotkeys.
- Overlay cards outside full screen.
- Per-card styling options.
