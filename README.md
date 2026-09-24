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
- **XP Calculator** calculates progress to a target level using `5 × level × (level + 1)` for next-level caps and `(5 / 3) × (level³ - level)` for cumulative XP.
- Usernames in the top-200 ranking resolve to profile IDs automatically. Ambiguous or out-of-ranking users can be linked with a verified profile ID or URL.

## XP Tracker

The tracker polls every minute and shows XP/hour, session XP, active class, XP to next level, and time-to-level estimates. Right-click a row to reset its XP/hour rate or all tracking data. Tracker behavior and overlays remain available inside the Plugins sidebar.

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

### Privacy and scoring

- **Opt-in:** The app sends a random installation ID, sharing preference, linked public profile IDs and names, and IDs with open game views. It does not send game credentials, local account IDs, or client-computed XP. Pilot opt-in is honor-based; ownership is not verified, so only share profiles you control. Request IPs may appear in normal logs and rate limits.
- **Sampling:** The service samples public profiles only while collection is enabled and a linked game view is active. The first sample sets a baseline; failed or uncertain samples add no XP.
- **Visibility:** Only current UTC periods are shown. Turning off sharing stops this device; gains already recorded remain visible until their periods end. Events are retained 31–40 days (40 by default); stale installations and unreferenced profile snapshots are cleaned up.

### Hosting and deployment

The service uses one Render Free web instance and a Neon Free PostgreSQL database. The Blueprint disables automatic deploys; migrations run at startup, and `/health/ready` checks database connectivity. Render Free services may sleep when idle. The current pilot samples every five minutes; change this to ten minutes before release.

Use the free Render and Neon plans; do not add a payment method or paid fallback without approval. Keep one service instance. Store the direct Neon connection string in Render as `ConnectionStrings__Leaderboard`, with `SSL Mode=VerifyFull`; never commit credentials. Keep the database secret and collection URL, interval, and enabled flag dashboard-managed (`sync: false`). New deployments should start with collection disabled, an empty profile URL, and a zero interval. Enable collection only after source permission and sampling cadence are approved, using an approved HTTPS profile URL containing `{playerId}`; restart the service after changing settings. See [Render Free limits](https://render.com/docs/free) and [Neon Free limits](https://neon.com/blog/neon-backend-is-ga); disable collection if quotas run low.

### Backup and recovery

Use PostgreSQL 16+ tools with Neon's direct TLS endpoint. Supply credentials through `PG*` environment variables and keep encrypted dumps private; they contain profile IDs and XP data.

```powershell
pg_dump --format=custom --no-owner --no-acl --file=leaderboard.dump
pg_restore --list leaderboard.dump
```

Restore into a new empty database, then verify table counts and `/health/ready` before switching the service. For rollback, disable collection and restart; deploy only an image compatible with the current schema because code rollback does not undo migrations. To erase data, disconnect clients, disable collection, purge the four application tables and provider backups, and remove local caches.
