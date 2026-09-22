# Fullscreen XP Overlay Edge-Tab Visibility and Shortcut

**Status:** Design approved; awaiting spec review.

**Amends:** [One-Above-Three Layout and Fullscreen XP Overlays](2026-09-22-one-above-three-xp-overlays-design.md)

## Goal

Keep the fullscreen XP overlay controls out of the way during play. After a user finishes placing overlays, the edge arrow should disappear and return only after a real layout change or an explicit global shortcut.

## Agreed behavior

- **Done** closes the tray, ends overlay editing, leaves placed overlays click-through, and hides the edge arrow.
- The arrow remains hidden across tracker refreshes, account changes, layout rebuilds that do not change the selected layout, and fullscreen exit/re-entry.
- A successfully saved and applied layout change reveals the arrow. A failed or canceled layout change does not.
- The global shortcut reveals only the arrow; it does not open the tray or enter editing. The user can then hover or activate the arrow to edit.
- Hiding is session-only. A fresh app launch begins with the arrow available the next time fullscreen is entered.

## Settings and global registration

- Add a configurable **Reveal XP overlay tab** key-chord control to the existing Settings dialog. The default is `Ctrl+Alt+Shift+O`.
- Persist the chord as an optional settings property so existing settings files load with the default. Store key and modifier data, not a localized display string.
- Register the shortcut through the Windows global-hotkey API (`RegisterHotKey`) against the manager window, and handle its `WM_HOTKEY` message. This must work while an embedded game/WebView has focus. Do not install a low-level keyboard hook.
- The Settings control records modifier keys plus a non-modifier key and displays the resulting chord. The user can replace it through Settings.
- When changing shortcuts, attempt to register the proposed chord before discarding the current registration or saving the new setting. If Windows reports a conflict, retain both the previously saved chord and its active registration, and show a clear error so the user can choose another.
- On startup, attempt to register the saved chord. If registration fails because another application owns it, keep the app usable, retain layout-change reveal, and indicate in Settings that the shortcut is unavailable. Unregister the active chord during normal shutdown.

## State ownership

The edge-tab visibility state is transient UI state owned by the fullscreen tray/control. The successful layout-selection flow and the global-hotkey message handler may request that the tab be shown. Ordinary tracker refreshes and fullscreen lifecycle setup must not reset a user-dismissed tab. The global-hotkey registration and settings chord are owned by the desktop window lifecycle; registration failure must not change overlay placement or click-through behavior.

## Verification

All automated tests remain under ignored `.local-tests/` and are excluded from tracked files, commits, and any PR. Cover default and round-trip shortcut settings, valid chord validation, visibility transitions (Done, successful/failed layout change, shortcut, ordinary refresh, fullscreen re-entry), and registration replacement/conflict/shutdown behavior using an injectable native-registration boundary where practical. Build the tracked solution normally.

Manual desktop checks cover recording and saving a chord, confirming the chord reveals only the arrow while the manager is not foreground, verifying Done/layout/refresh behavior, and checking the conflict message while the previous chord remains active. If an actual game client is available, verify the shortcut while it owns focus; do not authenticate or launch a user account solely for this test.

## Acceptance criteria

- Done removes both the tray and its edge arrow while keeping placed overlays visible and click-through.
- The edge arrow returns only after a successfully applied layout change or the configured global shortcut; both leave the tray closed.
- Fullscreen re-entry and incidental UI refreshes do not reveal a dismissed arrow.
- The shortcut works when the game/WebView has focus, is configurable, and defaults to `Ctrl+Alt+Shift+O`.
- Registration conflicts are surfaced without losing the prior saved or active chord; startup conflict does not prevent use of the app or layout-change reveal.
- Existing settings files load safely, automated tests remain local-only, and the solution builds.
