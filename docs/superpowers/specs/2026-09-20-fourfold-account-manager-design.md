# FourFold Account Manager Design

Date: 2026-09-20

## Goal

Create a Windows-only account manager for Sacred Seasons / FourFold Online. It should make it straightforward to keep several game accounts signed in independently and open them together in a configurable multi-box panel.

The current Fourfold Online site describes play in a browser: https://www.fourfoldonline.com/. The ATTACK Clicker README also identifies the game as a FourFold Online Firefox tab. The selected product direction is therefore an embedded browser manager, with a compatibility check before committing to the full panel.

## Product scope

The first version will:

- Manage named FourFold account profiles.
- Give every account its own persistent browser session.
- Let the user sign in manually through FourFold in that account's browser view.
- Show the official FourFold game in an embedded browser panel.
- Support three row-by-column layouts:
  - 1×2: one row, two columns.
  - 2×1: two rows, one column.
  - 2×2: two rows, two columns.
- Let users assign accounts to panel slots and launch the accounts in the selected layout.
- Preserve account metadata, session data, preferred layout, and slot assignments between app runs.
- Publish a Windows x64 build.

The first version will not include macros, OCR, automated input, activity history, game presets, multiple game targets, or password fields.

## Reuse from Roblox Account Manager

Use the Windows WPF client at C:\Users\Cody\Desktop\roblox-alt-launcher\client as the primary implementation reference. The RAM account profile model keeps display metadata separate from authentication data, and the Windows client uses isolated WebView2 profiles for account sessions. Adapt the account rail, profile management, local persistence, and session initialization patterns as small focused components.

Do not carry over RAM's game presets, activity log, plugin and automation system, Roblox-specific launch logic, or tab-only Clients panel. RAM's existing Clients panel displays one account view at a time; the simultaneous grid is new work.

The RAM working tree contains local modifications and untracked files. Treat it as read-only during this project. If source code is copied or derived, retain applicable Apache-2.0 license and NOTICE material.

## Application architecture

Build a focused WPF application targeting .NET 8 on Windows x64 and using Microsoft WebView2.

Keep responsibilities separate:

- Account profiles: stable account ID, user-visible label, display order, and basic favorite state.
- Account storage: JSON metadata under %LOCALAPPDATA%\FourFoldAccountManager.
- Browser sessions: one persistent WebView2 profile per account, stored under the app's local data directory. The manager does not collect or serialize account passwords or authentication tokens.
- Panel layout: layout selection, slot assignments, and rendering of two or four independent WebView2 views.
- FourFold navigation: one fixed official sign-in/game destination, resolved during the compatibility check; no editable game-preset system.

The UI should provide a compact account list beside the panel, a layout selector, slot assignment controls, and per-slot controls/status. Use the default 2×2 layout for the four-account use case. Switching between the two-column and two-row layouts changes orientation without changing slot identity. The 2×2 layout exposes all four saved slots; the two-cell layouts show and launch only the first two.

## Account and session flow

1. The user adds an account and gives it a label.
2. The app creates a stable account ID and an isolated WebView2 browser profile for that ID.
3. The user signs in on FourFold's own page inside that view. The WebView2 profile retains the site's session across app restarts.
4. The user assigns accounts to visible panel slots and selects a layout.
5. The user launches the assigned accounts. Each slot navigates in its own WebView2 session.
6. Closing the app preserves account metadata and browser session data.
7. Removing an account requires confirmation and clears that account's local browser profile.

The application will not inspect page content to infer login state. Users can see the current login/game page within each slot. Slot status is limited to transient states such as initializing, loading, active, and error. Errors appear beside the affected slot and can be retried; failure in one slot does not stop the others.

## Persistence

- accounts.json: account IDs, labels, favorite/display ordering.
- settings.json: selected layout, the four slot-to-account assignments, and ordinary window preferences.
- WebView2\Profiles\<account-id>: separate persistent website data for each account.

All data stays on the local Windows user profile. No account data is synced. Removing a profile deletes its associated website data after explicit confirmation.

## Compatibility and resource check

Before completing the grid implementation, verify that:

- FourFold sign-in and gameplay render and function in WebView2.
- Login state is isolated between two account profiles and persists after restart.
- A second window, popup, or external link required by the login flow has a clear, safe handling path.
- Two and four simultaneous game views remain usable on a normal desktop configuration.
- Each view can resize correctly in all three layouts at common Windows display scaling settings.

If the FourFold site cannot run correctly in WebView2 or four concurrent views are not practical, stop and revise the design before building the rest of the panel.

## Error and privacy behavior

- Browser initialization, navigation, and page failures are reported within the affected slot.
- Retrying a slot must preserve its account's existing browser profile.
- The manager must not log page contents, passwords, cookies, or authentication tokens.
- The UI must distinguish removing an account's metadata from deleting its stored browser session, and warn before the session data is erased.
- Closing one slot must not sign out or clear another account's session.

## High-level delivery stages

1. Create the account manager directory and separate private GitHub repository.
2. Verify the FourFold WebView2 sign-in flow and concurrent-view resource cost.
3. Build account profile management and independent persistent sessions.
4. Implement the 1×2, 2×1, and 2×2 panel layouts with slot assignment.
5. Add per-slot launch, close, retry, and concise status handling.
6. Publish and manually validate a Windows x64 build.

## Acceptance criteria

- The project exists in a directory and private GitHub repository separate from ATTACK Clicker and Roblox Account Manager.
- Users can create, label, reorder, and remove account profiles.
- Each profile keeps an independent FourFold login session across app restarts.
- The 1×2 and 2×1 layouts display two independent account views; 2×2 displays four.
- Switching layouts preserves account-to-slot assignments, and smaller layouts launch only visible slots.
- A failure in one view does not prevent other assigned accounts from opening.
- Account passwords are never collected or stored by the manager.
- The Windows build contains no macro, OCR, automated-input, activity-log, or game-preset feature.
