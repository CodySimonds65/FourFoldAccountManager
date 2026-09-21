# FourFold Account Manager

A Windows desktop app for managing FourFold accounts and opening them in separate browser sessions in a 1×2, 2×1, 2×2, or five-client 2×3 panel layout. Saved logins use Windows Credential Manager.

## Capabilities

- **Profile management** — Create, edit, favorite, reorder, and remove local FourFold profiles.
- **Panel layouts** — Assign accounts to 1×2, 2×1, 2×2, or five-client 2×3 panels and launch them together. The 2×3 layout has two equal-width clients above three equal-width clients, with a draggable saved row divider.
- **Game scaling** — Fit the entire game by default, fill panels to reduce black bars, and adjust each client’s viewport size.
- **Account launch and login** — Open isolated browser sessions, reuse saved credentials, complete sign-in automatically when possible, and fall back to manual action when needed.

Requires Windows, the .NET 10 SDK, and the Microsoft Edge WebView2 Runtime.

```powershell
dotnet build FourFoldAccountManager.sln -c Release
dotnet run --project src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj
```
