# FourFold Account Manager

A Windows desktop app for managing FourFold accounts and opening them in separate browser sessions in a 1×1, 1×2, 2×1, 2×2, five-client 2×3, or three-client 1×2 vertical panel layout. Saved logins use Windows Credential Manager.

## Capabilities

- **Profile management** — Create, edit, favorite, reorder, and remove local FourFold profiles.
- **Panel layouts** — Assign accounts to 1×1, 1×2, 2×1, 2×2, five-client 2×3, or three-client 1×2 vertical panels and launch them together. The 2×3 layout starts at a 60/40 row split with two equal-width clients above three equal-width clients. Its invisible boundary drag target preserves all client space in normal and full-screen modes, saves adjustments between 20/80 and 80/20, and restores them on launch. The 1×2 vertical layout places one full-height client on the left and two equal-height clients stacked on the right.
- **Game scaling** — Fit the entire game by default, fill panels to reduce black bars, and adjust each client’s viewport size.
- **Account launch and login** — Open isolated browser sessions, reuse saved credentials, complete sign-in automatically when possible, and fall back to manual action when needed.
- **XP tracker** — View earned XP/hour, session XP, and active-class XP remaining to the next level for each open client in a right-side panel. The panel is hidden in full screen.

## XP tracker

The tracker matches each open account to its public FourFold username. Use the saved login username or set a separate **Ranking username**; players outside the top 200 can be linked with a verified player ID or URL.

It polls every minute. The first sample sets the baseline; later samples show XP/hour, session XP, active class, and XP to next level. Failed polls show stale data and reset the baseline on the next success. Slot moves preserve tracking, closing a client resets it, and the panel is hidden in full screen.

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
