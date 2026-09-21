# FourFold Account Manager

A Windows desktop app for managing FourFold accounts and opening them in separate browser sessions in a 1×2, 2×1, 2×2, five-client 2×3, or three-client 1×2 vertical panel layout. Saved logins use Windows Credential Manager.

## Capabilities

- **Profile management** — Create, edit, favorite, reorder, and remove local FourFold profiles.
- **Panel layouts** — Assign accounts to 1×2, 2×1, 2×2, five-client 2×3, or three-client 1×2 vertical panels and launch them together. The 2×3 layout starts at a 60/40 row split with two equal-width clients above three equal-width clients. Its invisible boundary drag target preserves all client space in normal and full-screen modes, saves adjustments between 20/80 and 80/20, and restores them on launch. The 1×2 vertical layout places one full-height client on the left and two equal-height clients stacked on the right.
- **Game scaling** — Fit the entire game by default, fill panels to reduce black bars, and adjust each client’s viewport size.
- **Account launch and login** — Open isolated browser sessions, reuse saved credentials, complete sign-in automatically when possible, and fall back to manual action when needed.

Requires Windows, the .NET 10 SDK, and the Microsoft Edge WebView2 Runtime.

```powershell
dotnet build FourFoldAccountManager.sln -c Release
dotnet run --project src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj
```
