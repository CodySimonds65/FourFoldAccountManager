# XP per Hour Tracker Design

## Goal

Show earned XP per hour for each open FourFold client in a right-side panel of the Account Manager. The panel appears in every normal panel layout and disappears in full screen. Tracking follows the account, even if its slot changes.

## Existing app and source data

The Windows .NET 10 WPF app stores local account profiles by `Guid`, optional login credentials in Windows Credential Manager, and open WebView2 account IDs in `MainWindow._openAccountIds`. `MainContentGrid` has an account rail and a client grid. Full screen already collapses the account rail and chrome.

`https://fourfoldonline.com/ranking.php?mode=total_exp&limit=200` returns a server-rendered table with account names and `player.php?id=N` links. It lists at most 200 rows. Its score is not a safe earned-XP counter because it reflects XP currently held within classes and can fall after a level-up. Use this page to discover player IDs, not to calculate the rate.

Public `https://fourfoldonline.com/player.php?id=N` pages contain one `.class-card .class-body` per class with class name, level, current XP, XP needed for the next level, and an update time. The active class card is marked with an `Active Class` badge. At design time, sampled caps at levels 1, 10, 13, 31, 85, 207, and 286 all equal `5 × level × (level + 1)`. This is an observed rule, not a published contract. Every calculator input must pass that cap check; if the site changes the rule, the affected interval is unmeasurable rather than guessed.

## Identity

Add optional `RankingUsername` and `RankingPlayerId` fields to `AccountProfile`; old `accounts.json` files deserialize with both absent. Prefer an explicit ranking username, then the saved login username. Never assume the local profile label is the public username. A user can enter a public profile URL or numeric ID when the account is outside the ranking top 200. Validate the host and positive ID, fetch the profile, and verify that the public name matches the chosen ranking username before saving the link. A changed username invalidates a cached player ID until reverified. Account deletion clears its tracker data.

Match names after trimming and with ordinal case-insensitive comparison. Do not use fuzzy matching. Zero matches means unlinked; multiple distinct player IDs for the same normalized name means ambiguous. Never silently assign one of those IDs. A cached ID is checked against the profile name before XP is attributed to that account. Credentials remain in Credential Manager; tracker files contain public identifiers and XP snapshots only.

## Sampling and XP calculation

Start a tracking session when an account view opens. First successful profile fetch is the baseline. Poll every two minutes while at least one client is open: one ranking request, then one profile request per distinct open account, with bounded concurrency, a timeout, and cancellation on shutdown. Match new accounts from the ranking; use cached IDs for known accounts after verification. Do not poll while no views are open. Keep the most recent successful snapshot per account and a one-hour sequence of valid gain intervals in a local JSON file. A newly launched client starts a new session baseline even when older snapshots exist on disk. Use atomic writes and tolerate malformed tracker data by starting a fresh history without touching accounts or credentials.

For the same class and same level, gain is `newCurrentXp - oldCurrentXp` when nonnegative. For a higher level, gain is the XP remaining to finish the old level, plus the caps of fully crossed levels, plus XP into the new level. For example, level 13 at `900/910` followed by level 14 at `100/1050` yields `10 + 100 = 110 XP`. Sum valid class gains for the account. Calculate with checked 64-bit arithmetic. If a class disappears, a level falls, current XP falls without leveling, the observed cap disagrees with the rule, or the parsed profile is incomplete, mark that class interval invalid and rebaseline it. Keep gains from other valid classes. Do not report negative XP.

Display XP/hour from valid gain intervals in the trailing hour, dividing by the covered elapsed time and scaling to one hour. Clip a boundary-crossing interval proportionally to its overlap. Before the second valid sample, display `—` and `Collecting baseline`. A successful unchanged sample contributes zero XP and elapsed time. On a failed request, keep the last computed number with a visible `Stale` state and last successful update; do not extend the rate calculation until a valid new sample arrives. After a gap or invalid interval, show an uncertainty indicator and restart the affected baseline. Display session XP gained as the sum of measured, valid intervals only.

## UI and lifecycle

Add a scrollable right rail to `MainContentGrid`, with one compact row per open account in visible slot order. Each row shows slot, public username or an actionable linking state, XP/hour, session gained XP, the active class name, XP until that class's next level (`NextLevelXp - CurrentXp`), and freshness. A profile with no identifiable active class shows `—` for XP until next level rather than selecting an arbitrary class. The rail may contain a link/edit control for ranking username and manual profile URL. It is visible only when an account view is open and the window is not full screen. Entering full screen collapses both rail and gap grid columns; exiting restores them. Give the client grid sufficient minimum width at normal window sizes, with a compact right rail and a higher window minimum width if needed. Do not put tracker controls inside game slots or over the WebView2 content.

The coordinator listens to view-open, view-close, slot reassignment, layout rebuild, and app shutdown paths. It is keyed by account `Guid`, not slot index. Moving an open view keeps its baseline and history; closing a view ends the session and removes its row. Removing a local account removes its cached mapping and history. UI updates happen on the WPF dispatcher, and fetches never block the UI thread.

## Error and verification criteria

Parser failure, site timeout, HTTP error, missing ranking row, name mismatch, duplicate match, and invalid profile values each produce a clear row status. A failure for one account does not stop other accounts. Do not expose raw page content, credentials, or exception details in the UI.

Unit tests use saved, minimal HTML fixtures and cover ranking/profile parsing, active-class identification and XP remaining, exact matching, top-200 miss and manual link, normal and multiple level-ups, invalid caps, XP reset, rolling-window math, and session reset. Manual Windows verification covers all six panel layouts, a narrow normal window, opening and closing clients, slot changes, fullscreen entry/exit, and stale status. `dotnet build FourFoldAccountManager.sln -c Release` and the new test project must pass.
