# FourFold Account Manager

A Windows desktop app for managing FourFold accounts and opening them in separate browser sessions in a 1×1, 1×2, 2×1, 2×2, five-client 2×3, or three-client 1×2 vertical panel layout. Saved logins use Windows Credential Manager.

## Capabilities

- **Profile management** — Create, edit, favorite, reorder, and remove local FourFold profiles.
- **Panel layouts** — Assign accounts to 1×1, 1×2, 2×1, 2×2, five-client 2×3, or three-client 1×2 vertical panels and launch them together. The 2×3 layout starts at a 60/40 row split with two equal-width clients above three equal-width clients. Its invisible boundary drag target preserves all client space in normal and full-screen modes, saves adjustments between 20/80 and 80/20, and restores them on launch. The 1×2 vertical layout places one full-height client on the left and two equal-height clients stacked on the right.
- **Game scaling** — Fit the entire game by default, fill panels to reduce black bars, and adjust each client’s viewport size.
- **Account launch and login** — Open isolated browser sessions, reuse saved credentials, complete sign-in automatically when possible, and fall back to manual action when needed.
- **XP tracker** — View earned XP/hour, session XP, and active-class XP remaining to the next level for each open client in a right-side panel. The panel is hidden in full screen.

## XP tracker

The tracker matches each open account to its public FourFold username. It uses the saved login username unless you set a separate **Ranking username** in Edit profile. If the player is outside the ranking's top 200, enter their public `player.php?id=...` URL or numeric player ID there as well; the app verifies that the page's username matches before saving the link.

While a client is open, the app checks the public ranking and player pages every minute. The first player snapshot establishes a baseline, so XP/hour appears after a second successful check. The rate covers measured gains over the trailing hour; session XP counts measured gains since that client was opened. Level-up calculations include XP remaining in the old level and XP earned in the new one. The active class's **XP to next level** comes directly from its current and target XP on the player page.

If the public page cannot be refreshed, the row shows a stale status and the last successful update. The first successful check after a failed refresh sets a new baseline, so XP earned during the gap is excluded. Unexpected resets or changed level thresholds also mark an interval uncertain instead of adding guessed XP. Moving an open account between panel slots keeps its tracking session; closing and reopening it starts a new baseline. The right panel and its space disappear in full screen and return afterward.

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
