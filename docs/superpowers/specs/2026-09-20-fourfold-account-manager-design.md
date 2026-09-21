# FourFold Account Manager Design

Date: 2026-09-20

## Goal

Create a Windows-only account manager for Sacred Seasons / FourFold Online. It should make it straightforward to keep several game accounts signed in independently and open them together in a configurable multi-box panel.

The current Fourfold Online site describes play in a browser: https://www.fourfoldonline.com/. The ATTACK Clicker README also identifies the game as a FourFold Online Firefox tab. The selected product direction is therefore an embedded browser manager, with a compatibility check before committing to the full panel.

## Product scope

The first version will:

- Manage named FourFold account profiles.
- Give every account its own persistent browser session.
- Let the user save an optional username and password per account profile.
- Provide one **Launch accounts** action that opens assigned browser profiles in slot order, submits each profile's saved credentials on the official `/login.php` page, then opens FourFold in that isolated browser profile. Profiles without saved credentials remain on the sign-in page for manual use.
- Let users hide or restore the accounts rail so the multi-box panel can use the full window width.
- Let users hide slot selectors for started views and restore them with a global **Manage slots** control.
- Provide a **Full screen** focus mode that shows only the client layout, with an **Esc** shortcut and a visible exit control.
- Show the official FourFold game in an embedded browser panel.
- Support three row-by-column layouts:
  - 1×2: one row, two columns.
  - 2×1: two rows, one column.
  - 2×2: two rows, two columns.
- Let users assign accounts to panel slots and launch the accounts in the selected layout.
- Preserve account metadata, session data, preferred layout, and slot assignments between app runs.
- Publish a Windows x64 build.

The first version will not include macros, OCR, gameplay automation, activity history, game presets, multiple game targets, or credential export. Form submission is limited to saved credentials for assigned profiles after the user explicitly presses **Launch accounts**.

## Reuse from Roblox Account Manager

Use the Windows WPF client at C:\Users\Cody\Desktop\roblox-alt-launcher\client as the primary implementation reference. The RAM account profile model keeps display metadata separate from authentication data, and the Windows client uses isolated WebView2 profiles for account sessions. Adapt the account rail, profile management, local persistence, and session initialization patterns as small focused components.

Do not carry over RAM's game presets, activity log, plugin and automation system, Roblox-specific launch logic, or tab-only Clients panel. RAM's existing Clients panel displays one account view at a time; the simultaneous grid is new work.

The RAM working tree contains local modifications and untracked files. Treat it as read-only during this project. If source code is copied or derived, retain applicable Apache-2.0 license and NOTICE material.

## Application architecture

Build a focused WPF application targeting .NET 10 LTS on Windows x64 and using Microsoft WebView2.

Use .NET 10 LTS instead of matching RAM's .NET 8 target because the current official support policy lists .NET 8 end of support as November 10, 2026 and .NET 10 LTS end of support as November 14, 2028: https://dotnet.microsoft.com/en-us/platform/support/policy.

Keep responsibilities separate:

- Account profiles: stable account ID, user-visible label, display order, and basic favorite state.
- Account storage: JSON metadata under %LOCALAPPDATA%\FourFoldAccountManager; the JSON file contains no credentials.
- Credential storage: optional username/password pair stored as a Windows Credential Manager generic credential keyed by the stable account ID. Credentials are read only for editing or an explicit fill action and are never logged or exported.
- Browser sessions: one persistent WebView2 profile per account, stored under the app's local data directory. The manager never reads or serializes site cookies or authentication tokens.
- Panel layout: layout selection, slot assignments, and rendering of two or four independent WebView2 views.
- FourFold navigation: one fixed official sign-in/game destination, resolved during the compatibility check; no editable game-preset system.

The UI should provide a compact account list beside the panel, a layout selector, slot assignment controls, one global start action, and concise per-slot status. Avoid per-slot action rows. When a started profile's selector is hidden, show its account label; a global **Manage slots** control restores selectors for reassignment. Full screen hides the account rail, application header, toolbar, global status, and slot headers/status so only the WebView client grid remains visible. Use the default 2×2 layout for the four-account use case. Switching between the two-column and two-row layouts changes orientation without changing slot identity. The 2×2 layout exposes all four saved slots; the two-cell layouts show and launch only the first two.

## Account and session flow

The visual treatment uses a compact midnight-and-gold workspace: a 60-pixel header, a 232-pixel account rail, profile initials, and a single launch action. Slot numbers and small four-cell illustrations identify empty slots. Account and layout selectors display labels and open from their full surface. Routine ready/loaded status is hidden to preserve client space; progress, warnings, and errors remain visible. The profile editor uses the shared theme, and supported Windows 11 title bars match the app palette. Button hover transitions are brief, with clear keyboard focus outlines.

1. The user adds an account, gives it a label, and optionally saves a username/password pair.
2. The app writes profile metadata to JSON and stores any login in Windows Credential Manager under the profile's stable account ID.
3. The first unassigned visible panel slot is assigned to the new profile automatically.
4. The user assigns profiles to visible slots and presses **Launch accounts**. The manager visits `/login.php` sequentially in each assigned profile, submits the saved login only for profiles with stored credentials, then opens the existing browser start page. Profiles without stored credentials remain on sign-in for manual use; a failure in one slot does not stop the others. The selector is replaced by a compact account label after a browser view opens.
5. **Manage slots** restores selectors for reassignment. Hiding slot selectors does not change assignments.
6. The accounts rail can be hidden and restored without changing slot assignments or layout. Full screen hides all manager chrome around the client layout and exits with **Esc** or a visible control.
7. Closing the app preserves account metadata, Windows credentials, and browser data.
8. Removing an account requires confirmation and clears that account's local browser profile and saved credential.

The application will not inspect page content to infer login state. The batch action runs only on the exact official login page and locates its named username/password fields without reading their current values. It submits the matched form only after the user explicitly presses **Launch accounts**, waits for the FourFold authentication navigation, and then opens the configured browser start page. It leaves CSRF fields untouched and does not inspect the response or claim that authentication succeeded. Users can see the current login/game page within each slot. Slot status is limited to transient states such as initializing, loading, active, and error. Errors appear beside the affected slot; the user can retry with the global start action, and failure in one slot does not stop the others.

### Sign-in compatibility status

The current compatibility run showed all profiles logged out after reopening. Saved usernames/passwords provide a user-controlled way to fill the official login page, but do not override FourFold's session policy. The exact diagnostic executable, effective WebView2 data folder, stable profile names, and non-private setting are now recorded locally. Until a same-build restart test passes, saved browser authentication across restarts remains unresolved. Do not read, copy, or replay cookies or authentication tokens to work around site behavior. If FourFold does not retain sessions, disclose that the user may need to sign in again after restart.

## Persistence

- accounts.json: account IDs, labels, favorite/display ordering.
- Windows Credential Manager: optional username/password pair keyed to each account ID; removing a profile removes its saved credential.
- settings.json: selected layout, the four slot-to-account assignments, and ordinary window preferences.
- WebView2 user-data root: a stable named profile per account ID; the app does not depend on WebView2 internal folder names.

All data stays on the local Windows user profile. No account data is synced. Removing a profile deletes its associated website data and saved credential after explicit confirmation.

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
5. Add a global assigned-account start action, account-label slot headers, slot management controls, and concise status handling.
6. Add a full-screen panel focus mode and publish and manually validate a Windows x64 build.

## Acceptance criteria

- The project exists in a directory and private GitHub repository separate from ATTACK Clicker and Roblox Account Manager.
- Users can create, label, reorder, and remove account profiles.
- Each profile keeps an independent FourFold login session across app restarts.
- The 1×2 and 2×1 layouts display two independent account views; 2×2 displays four.
- A single **Launch accounts** action processes visible assigned profiles in slot order, automatically submitting only credentials saved for that profile and leaving profiles without saved credentials on the manual sign-in page.
- Started slots replace the account dropdown with a compact account label; a global **Manage slots** control restores selectors for reassignment.
- The accounts rail can be collapsed and restored without changing the panel layout or slot assignments.
- Full screen shows only the client layout; **Esc** or a visible exit control restores the regular manager window.
- Switching layouts preserves account-to-slot assignments, and smaller layouts launch only visible slots.
- A failure in one view does not prevent other assigned accounts from opening.
- Optional username/password pairs are stored in Windows Credential Manager and never in the JSON account file.
- A single global action starts assigned profiles; there are no per-slot action rows.
- The Windows build contains no macro, OCR, gameplay-input automation, activity-log, or game-preset feature.


