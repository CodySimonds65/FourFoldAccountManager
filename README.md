# FourFold Account Manager

A Windows desktop client for managing FourFold accounts, saved logins, multi-client layouts, and profile tools in separate browser sessions.

<details>
<summary><strong>Showcase</strong> — click to view screenshots</summary>

| Class Comparison | XP Calculator |
|---|---|
| <img src="./docs/showcase/class-comparison.png" alt="Class Comparison" width="320"> | <img src="./docs/showcase/xp-calculator.png" alt="XP Calculator" width="320"> |
| **Workspace Settings** |  |
| <img src="./docs/showcase/settings.png" alt="Workspace Settings" width="360"> |  |

</details>

## Features

- **Account profiles** — Create, edit, favorite, reorder, and remove local profiles. Saved credentials use Windows Credential Manager.
- **Flexible layouts** — Run 1×1, 1×2, 2×1, 2×2, 2×3, or vertical split client layouts together, or give each client its own tab.
- **Plugins** — Use XP Tracker, Class Comparison, and XP Calculator from the right-side Plugins sidebar.
- **Game controls** — Fit or fill each game panel, adjust viewport sizes, use full screen or theatre mode, and configure keyboard shortcuts.
- **Store blocking** — Turn on **Block the in-game store and gold buttons** in **Settings → Display** and those buttons do nothing in your panels. Without it, the store or gold page replaces the game in that panel.
- **Updates** — Stable Windows releases are published through [GitHub Releases](https://github.com/CodySimonds65/FourFoldAccountManager/releases).

## Profile tools

Choose an account directly from the dropdown on the **Stats** or **XP Calculator** tab. The selected profile refreshes automatically.

- **Class Comparison** reads the profile’s active class and compares its displayed HP, SP, ATT, MAG, SKL, SPD, LCK, DEF, and RES values against projected class averages. Equipment is shown for context only.
- **XP Calculator** shows the active class and calculates progress after you enter a target level, using `5 × level × (level + 1)` for next-level caps and `(5 / 3) × (level³ - level)` for cumulative XP. The target level is remembered per account and capped at level 9,999.
- Usernames in the top-200 ranking resolve to profile IDs automatically. Ambiguous or out-of-ranking users can be linked with a verified profile ID or URL.

## XP Tracker

The tracker polls every minute and measures the active class for XP/hour and session XP. It also shows XP to next level and time-to-level estimates. Right-click a row to reset its XP/hour rate or all tracking data. Tracker behavior remains available inside the Plugins sidebar. In full screen, open the edge tab (or press the reveal shortcut) to show the Overlays panel, switch each account's XP/hr, Stats, and XP calc cards on or off, and drag cards anywhere in the window, including the header in theatre mode. In the Tabs layout only the active tab's account shows its cards. The Stats card shows the active class's full stat comparison, and the XP calc card shows the XP and levels left to the target saved in the sidebar XP calc (or the next level). Both use the XP tracker's once-a-minute profile reads, so they add no extra requests. The button at the right of the toolbar collapses the Plugins sidebar; FourFold remembers the choice.

The **Timer** plugin is a speedrun stopwatch. Press the split shortcut to start a run and again to record each lap, the finish shortcut to stop, and the reset shortcut (or **Reset**) to clear it. The shortcuts default to Ctrl+Alt+Shift+S, F, and R and can be changed on the **Shortcuts** tab in Settings to any key, with or without Ctrl, Alt, or Shift, including F1–F12. A key bound on its own still reaches the game and other apps, and it fires even while Ctrl, Alt, or Shift is held, unless that exact combination is another shortcut. Because it isn't blocked, it also fires while you type it, for example in game chat, but not while you type in FourFold's own boxes. F5 can't be a shortcut, because it reloads a game panel whose browser misbehaves. In full screen, switch the Timer card on in the Overlays panel and drag it anywhere. Runs are not saved when the app closes.

## Tabs

Choose **Tabs** in the layout picker to give each account its own full-size tab. Press **+** to add a tab for a profile and **×** to close it, which also ends that game session. Games in background tabs keep running. Swap tabs by clicking them or with the Next tab and Previous tab shortcuts, which default to Ctrl+Alt+Shift+Right and Ctrl+Alt+Shift+Left and can be changed on the **Shortcuts** tab in Settings. In full screen the tab strip is hidden, so swap with the shortcuts. Opening the Leaderboard view slows every game until you return to the Workspace view. Switching to or from the Tabs layout closes any running game that is not shown in the new layout.

## Theatre mode

Press **Theatre** in the header, or the Toggle theatre mode shortcut (Ctrl+Alt+Shift+T by default), to hide the accounts panel, the Plugins sidebar, the toolbar, the tab strip, and each panel's header without leaving the window. The header row stays, and **Esc** or **Exit theatre** returns to the normal view. Your overlay cards and the edge tab work in theatre mode the same as in full screen; turn that off with **Show overlays in theatre mode** in Settings. You can go full screen from theatre mode, and Esc then returns you to theatre mode.

## Second monitor

Choose how to use a second monitor in **Settings → Display → Second monitor**.

- **Account tools window** (the default): press **Pop out tools** in the header, or in the Overlays panel in theatre mode and full screen, to move the Account tools panel into its own window, then drag it to another monitor. Close that window to put the panel back. FourFold remembers whether it was open and where, and reopens it there; if that monitor is gone, it opens on the main monitor.
- **Floating cards**: press **Floating cards** to choose which overlay cards float. A floating card stays on top of every window on any monitor, never takes focus, and lets clicks pass through to whatever is underneath. Tick **Arrange floating cards** in the same menu to drag and resize them, and untick it to lock them again. A card is either over the game or floating: switching it on in the Overlays panel moves it back over the game, each to its own saved spot. Floating cards hide while FourFold is minimized.

Switching modes closes the tools window or puts floating cards back over the game; saved positions are kept.

## Requirements

- Windows
- .NET 10 SDK/runtime
- Microsoft Edge WebView2 Runtime

## Run from source

```powershell
dotnet build FourFoldAccountManager.sln -c Release
dotnet run --project src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj
```

Windows releases include a framework-dependent ZIP, standalone executable, and checksums file. The release workflow currently publishes `win-x64` assets only.

## Shared XP leaderboard

The Workspace tab ranks XP gained during the current UTC day, ISO week, or calendar month. The desktop uses `https://fourfold-shared-xp-leaderboard.onrender.com` by default; developers can override it with `FOURFOLD_LEADERBOARD_URL`. Local XP tracking works without the service.

Session XP and leaderboard XP cover different scopes: the tracker counts the active class from its local session start or reset, while the leaderboard totals valid gains across all classes in the selected UTC period after its first baseline. A saved leaderboard page may lag; interrupted participation or uncertain samples are not backfilled.

When an opted-in profile launches, its first heartbeat wakes the server, which reads that player's public profile right away, ahead of routine samples. That first server-verified observation records zero gained XP and lines up with the start of the desktop session within seconds. After that, each active player is sampled on its own `Collection:MinimumSampleInterval` cadence (five minutes in production), with consecutive site requests spaced by `Collection:RequestSpacing` (two seconds by default). A failed read waits one full interval before it is retried. Repeated heartbeats do not reset an established baseline, and the server does not accept an XP value from the client.

### Backup and recovery

Use PostgreSQL 16+ tools with Neon's direct TLS endpoint. Supply credentials through `PG*` environment variables and keep encrypted dumps private; they contain profile IDs and XP data.

```powershell
pg_dump --format=custom --no-owner --no-acl --file=leaderboard.dump
pg_restore --list leaderboard.dump
```

Restore into a new empty database, then verify table counts and `/health/ready` before switching the service. For rollback, disable collection and restart; deploy only an image compatible with the current schema because code rollback does not undo migrations. To erase data, disconnect clients, disable collection, purge the four application tables and provider backups, and remove local caches.

## License

Copyright 2026 Cody Simonds. Licensed under the [Apache License 2.0](LICENSE).
