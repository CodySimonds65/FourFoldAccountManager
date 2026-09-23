# FourFold Account Manager

A Windows desktop app for managing FourFold accounts and opening them in separate browser sessions in a 1×1, 1×2, 2×1, 2×2, five-client 2×3, or three-client 1×2 vertical panel layout. Saved logins use Windows Credential Manager.

## Capabilities

- **Profile management** — Create, edit, favorite, reorder, and remove local FourFold profiles.
- **Panel layouts** — Assign accounts to 1×1, 1×2, 2×1, 2×2, five-client 2×3, or three-client 1×2 vertical panels and launch them together. Every multi-client layout has shared drag boundaries: resizing one client redistributes space among the clients in that split group so panels do not overlap, and each affected client remains at least 30% of the group. Split positions are saved independently for each layout and restored on launch. Use **Settings → Reset layout sizes** to restore the default boundaries; this resets layout splits only and leaves per-client viewport sizes unchanged. The 1×1 layout has no resize boundaries. The 2×3 layout starts at a 60/40 row split with two equal-width clients above three equal-width clients. The 1×2 vertical layout places one full-height client on the left and two equal-height clients stacked on the right.
- **Game scaling** — Fit the entire game by default, fill panels to reduce black bars, and adjust each client’s viewport size.
- **Account launch and login** — Launch accounts starts assigned sessions whose views are missing or marked failed, leaving healthy open sessions untouched. Each open, assigned slot has a relaunch icon over its slot number on hover or keyboard focus for a game that appears stuck; not every game freeze is automatically detected. Relaunching restarts only that account's browser view and reuses its saved browser profile, though FourFold may ask you to sign in again.
- **Plugins** — Use the reusable right-side Plugins sidebar for XP Tracker, Class Comparison, and XP Calculator. The sidebar is hidden in full screen.
- **XP tracker** — View earned XP/hour, session XP, and active-class XP remaining to the next level for each open client. The existing tracker rate, reset, and overlay behavior is preserved.
- **Class Comparison** — Read the selected account's public profile automatically, select its dynamically reported active class, and compare the displayed base HP, SP, ATT, MAG, SKL, SPD, LCK, DEF, and RES values with projected averages for that class. Equipment names are shown as context only and do not change the comparison.
- **XP Calculator** — Select a target level for the selected profile and calculate remaining XP using `5 × level × (level + 1)` for each next-level cap and `(5 / 3) × (level³ - level)` for cumulative XP. Current in-level EXP is subtracted when its cap is valid; otherwise the calculator clearly uses the start of the current level.

## XP tracker

The tracker matches each open account to its public FourFold username. Use the saved login username or set a separate **Ranking username**; players outside the top 200 can be linked with a verified player ID or URL.

The Plugins sidebar uses the same public-profile identity flow. A unique username in the top-200 ranking resolves automatically to its linked profile ID, and that ID is cached after a verified profile read. Ambiguous names, players outside the top 200, and profile-name mismatches keep the existing manual-link guidance.

It polls every minute. The first sample sets the baseline; later samples show XP/hour, session XP, active class, XP to next level, and an active-class time-to-level estimate based on the current XP/hour. Level gains are calculated continuously across level transitions, so XP earned after a level-up remains part of the session total and rate. Failed polls show stale data and reset the baseline on the next success. Slot moves preserve tracking, closing a client resets it, and the panel is hidden in full screen.

Right-click a tracker row for **Reset XP/hr** or **Reset all**. **Reset XP/hr** clears that row's rate intervals and baseline while preserving session XP; **Reset all** also clears the row's session XP and rate data.

Class Comparison and XP Calculator refresh the selected account's public profile when opened or when the account selection changes. Account switching clears the previous profile immediately, and a failed same-account refresh leaves the last valid snapshot visible with the failure status.

Requires Windows, the .NET 10 SDK, and the Microsoft Edge WebView2 Runtime.

## Updates and releases

Stable Windows releases are published on [GitHub Releases](https://github.com/CodySimonds65/FourFoldAccountManager/releases). FourFold checks once after startup, asks before downloading a newer version, verifies the standalone executable, and restarts into the update when you approve it. WebView2 remains required after updating.

Each Windows release includes:

```text
FourFoldAccountManager-v<version>-win-x64.zip
FourFoldAccountManager-v<version>-win-x64-standalone.exe
FourFoldAccountManager-v<version>-checksums.txt
```

The release workflow currently publishes Windows `win-x64` assets only; no macOS build is provided.

```powershell
dotnet build FourFoldAccountManager.sln -c Release
dotnet run --project src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj
```
