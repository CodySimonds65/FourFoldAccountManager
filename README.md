# FourFold Account Manager

A Windows desktop app for managing FourFold accounts and opening them in separate browser sessions in a 1×2, 2×1, or 2×2 panel layout. Saved logins use Windows Credential Manager.

Requires Windows, the .NET 10 SDK, and the Microsoft Edge WebView2 Runtime.

```powershell
dotnet build FourFoldAccountManager.sln -c Release
dotnet run --project src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj
```
