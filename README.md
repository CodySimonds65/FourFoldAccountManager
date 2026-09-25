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
- **Flexible layouts** — Run 1×1, 1×2, 2×1, 2×2, 2×3, or vertical split client layouts together.
- **Plugins** — Use XP Tracker, Class Comparison, and XP Calculator from the right-side Plugins sidebar.
- **Game controls** — Fit or fill each game panel, adjust viewport sizes, use full screen, and configure keyboard shortcuts.
- **Updates** — Stable Windows releases are published through [GitHub Releases](https://github.com/CodySimonds65/FourFoldAccountManager/releases).

## Profile tools

Choose an account directly from the dropdown on the **Stats** or **XP Calculator** tab. The selected profile refreshes automatically.

- **Class Comparison** reads the profile’s active class and compares its displayed HP, SP, ATT, MAG, SKL, SPD, LCK, DEF, and RES values against projected class averages. Equipment is shown for context only.
- **XP Calculator** shows the active class and calculates progress after you enter a target level, using `5 × level × (level + 1)` for next-level caps and `(5 / 3) × (level³ - level)` for cumulative XP.
- Usernames in the top-200 ranking resolve to profile IDs automatically. Ambiguous or out-of-ranking users can be linked with a verified profile ID or URL.

## XP Tracker

The tracker polls every minute and measures the active class for XP/hour and session XP. It also shows XP to next level and time-to-level estimates. Right-click a row to reset its XP/hour rate or all tracking data. Tracker behavior remains available inside the Plugins sidebar. In full screen, open the edge tab (or press the reveal shortcut) to show the Overlays panel, switch each account's XP/hr, Stats, and XP calc cards on or off, and drag cards into place. The Stats card shows the active class's full stat comparison, and the XP calc card shows the XP and levels left to the target saved in the sidebar XP calc (or the next level). Both use the XP tracker's once-a-minute profile reads, so they add no extra requests. The button at the right of the toolbar collapses the Plugins sidebar; FourFold remembers the choice.

The **Timer** plugin is a speedrun stopwatch. Press the split shortcut to start a run and again to record each lap, the finish shortcut to stop, and the reset shortcut (or **Reset**) to clear it. The shortcuts default to Ctrl+Alt+Shift+S, F, and R and can be changed in Settings, including to a single numpad, F13–F24, Pause, Scroll Lock, or Insert key. A key bound on its own stops reaching games and other apps while FourFold is open. Single-key shortcuts only fire when no Ctrl, Alt, or Shift key is held, so avoid holding a modifier while splitting. In full screen, switch the Timer card on in the Overlays panel and drag it anywhere. Runs are not saved when the app closes.

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
