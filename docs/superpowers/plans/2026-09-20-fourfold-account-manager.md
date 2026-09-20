# FourFold Account Manager Implementation Plan

> For agentic workers: REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Build a Windows-only FourFold account manager with separate persistent sessions and 1×2, 2×1, and 2×2 embedded browser layouts.

**Architecture:** A small .NET 10 Core library owns account metadata, local JSON persistence, and panel rules. A WPF desktop app owns WebView2 sessions, account controls, and the embedded panel. Each stable account ID maps to one persistent WebView2 profile; the manager does not handle passwords or auth tokens.

**Tech Stack:** .NET 10 LTS; WPF; Microsoft.Web.WebView2 1.0.4078.44 (the pinned version in RAM's Windows client); System.Text.Json; MSTest 4.3.3 and Microsoft.NET.Test.Sdk 18.9.0.

**Spec:** docs/superpowers/specs/2026-09-20-fourfold-account-manager-design.md

## Global Constraints

- Windows-only account manager.
- Build a focused WPF application targeting .NET 10 LTS on Windows x64 and using Microsoft WebView2.
- Give every account its own persistent browser session.
- The user signs in manually through FourFold in that account's browser view.
- The manager does not collect or serialize account passwords or authentication tokens.
- Use one fixed official sign-in/game destination, resolved during the compatibility check; no editable game-preset system.
- Support 1×2, 2×1, and 2×2 row-by-column layouts.
- Store JSON metadata under %LOCALAPPDATA%\FourFoldAccountManager and separate persistent WebView2 data per account.
- No macros, OCR, automated input, activity history, game presets, multiple game targets, or password fields.
- Keep the ATTACK Clicker and RAM repositories untouched; retain applicable Apache-2.0 license and NOTICE material for any code derived from RAM.

## Review Focus

- Malformed account JSON or duplicate IDs must surface an error without overwriting the original data (Task 2 tests).
- Switching from 2×2 to a two-slot layout and back must preserve hidden assignments while launching only visible slots (Tasks 3 and 5 tests/manual checks).
- Assigning the same account twice moves it from its old slot rather than opening one profile twice (Task 3 tests).
- A failure in one browser must preserve its session and leave other slots usable (Tasks 4 and 6 manual checks).
- Popups, non-HTTPS links, and unapproved hosts must be blocked or handled visibly without leaving the account profile (Task 4 tests/manual checks).

## File Map

- FourFoldAccountManager.sln contains Core, Desktop, and Core.Tests.
- src/FourFoldAccountManager.Core/ contains account/profile rules, local data stores, layout policy, and URL policy.
- src/FourFoldAccountManager.Desktop/ contains WPF startup, WebView2 session service, view models, and account/panel controls.
- tests/FourFoldAccountManager.Core.Tests/ contains MSTest coverage for persistence, slot rules, and URL policy.
- tools/FourFoldWebViewProbe/ is a temporary compatibility prototype; remove it after recording successful findings.
- docs/fourfold-webview-compatibility.md records required HTTPS origins, popup behavior, session isolation, and four-view responsiveness.

---

### Task 1: Create the Private Repository and Validate WebView2

**Files:**
- Create: .gitignore
- Create: FourFoldAccountManager.sln
- Create: src/FourFoldAccountManager.Core/FourFoldAccountManager.Core.csproj
- Create: src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj
- Create: src/FourFoldAccountManager.Desktop/App.xaml
- Create: src/FourFoldAccountManager.Desktop/App.xaml.cs
- Create: src/FourFoldAccountManager.Desktop/MainWindow.xaml
- Create: src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs
- Delete: src/FourFoldAccountManager.Core/Class1.cs
- Delete: tests/FourFoldAccountManager.Core.Tests/UnitTest1.cs
- Create: tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj
- Create: tools/FourFoldWebViewProbe/FourFoldWebViewProbe.csproj
- Create: tools/FourFoldWebViewProbe/MainWindow.xaml
- Create: tools/FourFoldWebViewProbe/MainWindow.xaml.cs
- Create: docs/fourfold-webview-compatibility.md

**Interfaces:**
- Consumes: the approved design spec.
- Produces: private remote CodySimonds65/FourFoldAccountManager; the .NET 10 solution skeleton; compatibility findings with exact allowed HTTPS origins.

- [ ] Step 1: Create and verify the private GitHub remote.

Run:
~~~powershell
gh auth status
gh repo create CodySimonds65/FourFoldAccountManager --private --source . --remote origin --push
gh repo view CodySimonds65/FourFoldAccountManager --json name,visibility
~~~
Expected: GitHub CLI is authenticated, visibility is PRIVATE, and origin points to this new repo.

- [ ] Step 2: Create the feature branch and solution projects.

Run:
~~~powershell
git switch -c feature/fourfold-account-manager
dotnet new gitignore
dotnet new sln -n FourFoldAccountManager --format sln
dotnet new classlib -n FourFoldAccountManager.Core -o src/FourFoldAccountManager.Core -f net10.0
dotnet new wpf -n FourFoldAccountManager.Desktop -o src/FourFoldAccountManager.Desktop -f net10.0
dotnet new mstest -n FourFoldAccountManager.Core.Tests -o tests/FourFoldAccountManager.Core.Tests -f net10.0
dotnet sln FourFoldAccountManager.sln add src/FourFoldAccountManager.Core/FourFoldAccountManager.Core.csproj src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj
dotnet add src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj reference src/FourFoldAccountManager.Core/FourFoldAccountManager.Core.csproj
dotnet add tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj reference src/FourFoldAccountManager.Core/FourFoldAccountManager.Core.csproj
dotnet add tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj package Microsoft.NET.Test.Sdk --version 18.9.0
dotnet add tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj package MSTest.TestAdapter --version 4.3.3
dotnet add tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj package MSTest.TestFramework --version 4.3.3
dotnet add src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj package Microsoft.Web.WebView2 --version 1.0.4078.44
dotnet build FourFoldAccountManager.sln
~~~
Expected: Core, Desktop, and Core.Tests restore and build under the installed .NET 10 SDK.

- [ ] Step 3: Create a temporary WPF probe with four independent WebView2 profiles.

~~~csharp
var environment = await CoreWebView2Environment.CreateAsync(
    browserExecutableFolder: null,
    userDataFolder: Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FourFoldAccountManager", "Probe"));

foreach (var accountId in ProbeAccountIds)
{
    var options = environment.CreateCoreWebView2ControllerOptions();
    options.ProfileName = accountId.ToString("N");
    options.IsInPrivateModeEnabled = false;
    var view = new WebView2();
    await view.EnsureCoreWebView2Async(environment, options);
    view.CoreWebView2.Settings.AreDevToolsEnabled = false;
    view.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
    view.CoreWebView2.Navigate("https://www.fourfoldonline.com/");
    ProbeGrid.Children.Add(view);
}
~~~

Use four synthetic stable IDs. Do not include credential fields or record secrets.

- [ ] Step 4: Run the probe and manually verify the site flow.

Run: dotnet run --project tools/FourFoldWebViewProbe/FourFoldWebViewProbe.csproj
Expected: FourFold loads in all four views. Sign in manually to two user-controlled accounts in separate profiles, confirm sessions do not cross, restart the probe and confirm persistence. Record required popup/redirect origins and responsiveness only; never record passwords, cookies, tokens, or page contents.

- [ ] Step 5: Record findings and remove the temporary probe source.

Write docs/fourfold-webview-compatibility.md with observed origins, popup behavior, login persistence, and whether four views are usable. If the site cannot work in WebView2 or four views are impractical, stop and revise the design before building the account UI. After verifying the repository root, remove only tools/FourFoldWebViewProbe from the project.

- [ ] Step 6: Build the retained projects and commit the bootstrap.

Run:
~~~powershell
dotnet build FourFoldAccountManager.sln
git add .gitignore FourFoldAccountManager.sln src tests docs/fourfold-webview-compatibility.md
git commit -m "chore: bootstrap FourFold manager"
~~~

### Task 2: Implement Account Profiles and Local Persistence

**Files:**
- Create: src/FourFoldAccountManager.Core/Models/AccountProfile.cs
- Create: src/FourFoldAccountManager.Core/Models/AccountProfileRules.cs
- Create: src/FourFoldAccountManager.Core/Data/LocalDataPaths.cs
- Create: src/FourFoldAccountManager.Core/Data/AccountStore.cs
- Create: tests/FourFoldAccountManager.Core.Tests/AccountProfileRulesTests.cs
- Create: tests/FourFoldAccountManager.Core.Tests/AccountStoreTests.cs

**Interfaces:**
- Consumes: Core project from Task 1.
- Produces: AccountProfile.Create(string label, int sortOrder = 0); AccountProfileRules.NormalizeLabel(string label); LocalDataPaths(string? root = null); AccountStore.LoadAsync(CancellationToken); AccountStore.SaveAsync(IReadOnlyCollection<AccountProfile>, CancellationToken).

- [ ] Step 1: Add failing model and store tests.

~~~csharp
[TestClass]
public sealed class AccountProfileRulesTests
{
    [TestMethod]
    public void Create_TrimsLabelAndGeneratesId()
    {
        var account = AccountProfile.Create("  Main  ");
        Assert.AreEqual("Main", account.Label);
        Assert.AreNotEqual(Guid.Empty, account.Id);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void NormalizeLabel_RejectsBlankInput(string label)
    {
        Assert.ThrowsException<ArgumentException>(
            () => AccountProfileRules.NormalizeLabel(label));
    }
}
~~~

Add AccountStore cases for missing-file default, save/load round-trip, duplicate or empty ID rejection, and malformed JSON preservation. Use a unique temporary data root per test.

- [ ] Step 2: Run only the new account tests and confirm the failures.

Run: dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~Account
Expected: failures for missing types or behaviors before implementation.

- [ ] Step 3: Implement profile rules and atomic JSON storage.

~~~csharp
public sealed record AccountProfile(Guid Id, string Label, bool IsFavorite, int SortOrder)
{
    public static AccountProfile Create(string label, int sortOrder = 0) =>
        new(Guid.NewGuid(), AccountProfileRules.NormalizeLabel(label), false, sortOrder);
}

public static class AccountProfileRules
{
    public const int MaximumLabelLength = 60;

    public static string NormalizeLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var normalized = label.Trim();
        if (normalized.Length > MaximumLabelLength)
            throw new ArgumentException(
                $"Labels may contain at most {MaximumLabelLength} characters.", nameof(label));
        return normalized;
    }
}
~~~

Use LocalDataPaths below %LOCALAPPDATA%\FourFoldAccountManager. Missing accounts.json loads an empty list. Malformed JSON or duplicate/empty IDs raises InvalidDataException without changing the file. Save through a temporary sibling file, flush, and replace the destination.

- [ ] Step 4: Run the account tests and commit the result.

Run: dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~Account
Expected: PASS; corrupt input remains byte-for-byte unchanged.
Commit: git add src/FourFoldAccountManager.Core tests/FourFoldAccountManager.Core.Tests; git commit -m "feat: add local account profiles"

### Task 3: Implement the Three Layouts and Persisted Slot Assignments

**Files:**
- Create: src/FourFoldAccountManager.Core/Models/PanelLayout.cs
- Create: src/FourFoldAccountManager.Core/Models/PanelSettings.cs
- Create: src/FourFoldAccountManager.Core/Panel/PanelLayoutPolicy.cs
- Create: src/FourFoldAccountManager.Core/Data/SettingsStore.cs
- Create: tests/FourFoldAccountManager.Core.Tests/PanelLayoutPolicyTests.cs
- Create: tests/FourFoldAccountManager.Core.Tests/SettingsStoreTests.cs

**Interfaces:**
- Consumes: LocalDataPaths and account IDs from Task 2.
- Produces: PanelLayout.OneByTwo, TwoByOne, TwoByTwo; GridDimensions GetDimensions(PanelLayout); GetVisibleSlotCount(PanelLayout); Assign(PanelSettings, int, Guid?); ClearAccount(PanelSettings, Guid); SettingsStore.LoadAsync/SaveAsync.

- [ ] Step 1: Add failing policy and persistence tests.

~~~csharp
[TestClass]
public sealed class PanelLayoutPolicyTests
{
    [DataTestMethod]
    [DataRow(PanelLayout.OneByTwo, 1, 2, 2)]
    [DataRow(PanelLayout.TwoByOne, 2, 1, 2)]
    [DataRow(PanelLayout.TwoByTwo, 2, 2, 4)]
    public void GetDimensions_ReturnsRowsColumnsAndCapacity(
        PanelLayout layout, int rows, int columns, int capacity)
    {
        Assert.AreEqual(rows, PanelLayoutPolicy.GetDimensions(layout).Rows);
        Assert.AreEqual(columns, PanelLayoutPolicy.GetDimensions(layout).Columns);
        Assert.AreEqual(capacity, PanelLayoutPolicy.GetVisibleSlotCount(layout));
    }
}
~~~

Also test that assigning an account to a second slot moves it from the first, changing layout preserves hidden slots, ClearAccount clears matching IDs, and settings round-trip.

- [ ] Step 2: Run the focused tests and confirm they fail before implementation.

Run: dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~PanelLayout
Expected: FAIL for missing layout behavior.

- [ ] Step 3: Implement layout policy and settings storage.

~~~csharp
public enum PanelLayout { OneByTwo, TwoByOne, TwoByTwo }
public readonly record struct GridDimensions(int Rows, int Columns);

public static int GetVisibleSlotCount(PanelLayout layout) =>
    layout switch
    {
        PanelLayout.OneByTwo or PanelLayout.TwoByOne => 2,
        PanelLayout.TwoByTwo => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(layout))
    };
~~~

PanelSettings stores exactly four account IDs and defaults to TwoByTwo. Assign copies the slot array, removes that account from any prior slot, then assigns it at the requested index. Layout changes do not change the slot array. Two-cell layouts expose slots zero and one only.

- [ ] Step 4: Run panel tests and commit.

Run: dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~PanelLayout
Expected: PASS for orientation, capacity, uniqueness, hidden-slot preservation, deletion cleanup, and persistence.
Commit: git add src/FourFoldAccountManager.Core tests/FourFoldAccountManager.Core.Tests; git commit -m "feat: add multi-box layout settings"

### Task 4: Implement Navigation Policy and Persistent WebView2 Sessions

**Files:**
- Create: src/FourFoldAccountManager.Core/Navigation/FourFoldNavigationPolicy.cs
- Create: tests/FourFoldAccountManager.Core.Tests/FourFoldNavigationPolicyTests.cs
- Create: src/FourFoldAccountManager.Desktop/Services/FourFoldDestination.cs
- Create: src/FourFoldAccountManager.Desktop/Services/AccountBrowserSessionService.cs

**Interfaces:**
- Consumes: LocalDataPaths and account IDs from Task 2; allowed hosts from docs/fourfold-webview-compatibility.md.
- Produces: FourFoldNavigationPolicy(IEnumerable<string> allowedHosts).TryValidate(Uri, out Uri); FourFoldDestination.StartUri; AccountBrowserSessionService.CreateViewAsync(Guid, CancellationToken) returning Task<WebView2>, NavigateAsync(Guid, Uri, CancellationToken) returning Task, CloseViewAsync(Guid) returning Task, and ClearProfileAsync(Guid, CancellationToken) returning Task.

- [ ] Step 1: Add URL-policy tests for allowed and rejected destinations.

~~~csharp
[TestClass]
public sealed class FourFoldNavigationPolicyTests
{
    private readonly FourFoldNavigationPolicy _policy =
        new(["game.example.test", "login.example.test"]);

    [TestMethod]
    public void TryValidate_AllowsHttpsOnConfiguredHost()
    {
        Assert.IsTrue(_policy.TryValidate(
            new Uri("https://game.example.test/"), out var approved));
        Assert.AreEqual(Uri.UriSchemeHttps, approved.Scheme);
    }

    [DataTestMethod]
    [DataRow("http://game.example.test/")]
    [DataRow("javascript:alert(1)")]
    [DataRow("https://outside.example/")]
    [DataRow("https://user:secret@game.example.test/")]
    public void TryValidate_RejectsUnsafeUri(string value) =>
        Assert.IsFalse(_policy.TryValidate(new Uri(value), out _));
}
~~~

- [ ] Step 2: Run URL-policy tests to confirm they fail before implementation.

Run: dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~FourFoldNavigation
Expected: FAIL for the missing policy.

- [ ] Step 3: Implement an exact HTTPS host allow-list and fixed FourFold destination.

Normalize host casing and reject user-info, non-HTTPS schemes, unknown hosts, and invalid URIs. Use the exact origins found in Task 1. Do not inspect game page content or infer login state.

- [ ] Step 4: Implement the shared WebView2 environment and unique profile per account.

~~~csharp
var environment = await CoreWebView2Environment.CreateAsync(
    browserExecutableFolder: null,
    userDataFolder: _paths.WebViewUserDataRoot);
var options = environment.CreateCoreWebView2ControllerOptions();
options.ProfileName = accountId.ToString("N");
options.IsInPrivateModeEnabled = false;

var view = new WebView2();
await view.EnsureCoreWebView2Async(environment, options);
view.CoreWebView2.Settings.AreDevToolsEnabled = false;
view.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
~~~

Keep one environment per app process and use each stable account GUID as ProfileName. Cache live controls by account ID. Closing a view releases its controller without clearing persistent session data.

- [ ] Step 5: Run URL tests and manually confirm two profiles remain isolated across restart.

Run: dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --filter FullyQualifiedName~FourFoldNavigation
Manual check: sign in manually in two account profiles, confirm no cross-login, close/reopen both views, and confirm each session persists independently.

- [ ] Step 6: Commit the navigation and session service.

Commit: git add src tests/FourFoldAccountManager.Core.Tests; git commit -m "feat: add isolated FourFold web sessions"

### Task 5: Build Account Controls and the Multi-Box Panel

**Files:**
- Create: src/FourFoldAccountManager.Desktop/ViewModels/MainViewModel.cs
- Create: src/FourFoldAccountManager.Desktop/ViewModels/PanelSlotViewModel.cs
- Create: src/FourFoldAccountManager.Desktop/Infrastructure/RelayCommand.cs
- Create: src/FourFoldAccountManager.Desktop/Resources/Theme.xaml
- Create: src/FourFoldAccountManager.Desktop/Views/AddAccountDialog.xaml
- Create: src/FourFoldAccountManager.Desktop/Views/AddAccountDialog.xaml.cs
- Create: src/FourFoldAccountManager.Desktop/Views/PanelSlotControl.xaml
- Create: src/FourFoldAccountManager.Desktop/Views/PanelSlotControl.xaml.cs
- Modify: src/FourFoldAccountManager.Desktop/MainWindow.xaml
- Modify: src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs
- Modify: src/FourFoldAccountManager.Desktop/App.xaml.cs
- Create: src/FourFoldAccountManager.Desktop/Services/PanelLaunchCoordinator.cs

**Interfaces:**
- Consumes: AccountStore from Task 2; SettingsStore and PanelLayoutPolicy from Task 3; browser and destination services from Task 4.
- Produces: MainViewModel account commands; four stable PanelSlotViewModel instances (SlotIndex, AccountId, State, ErrorMessage); PanelLaunchCoordinator.LaunchVisibleAsync(PanelSettings, CancellationToken).

- [ ] Step 1: Implement the account rail and label-only add dialog.

Display account labels, favorites, and actions to add, rename, reorder, and favorite profiles. Reuse the RAM account-rail visual hierarchy with a restrained FourFold theme, clear spacing, and readable status contrast. Add the Remove action in Task 6 after profile-data clearing is available. The add dialog accepts a display label only; it has no username/password fields. Persist changes to accounts.json immediately.

- [ ] Step 2: Add the three-layout selector and four stable slot assignments.

Provide selectable 1×2, 2×1, and 2×2 layouts. Bind slot pickers to accounts; route changes through PanelLayoutPolicy.Assign so the same account cannot appear twice. Keep all four assignments in settings when changing to a two-cell layout.

- [ ] Step 3: Render the current grid and embed account views.

Use WPF Grid row/column definitions from PanelLayoutPolicy.GetDimensions. Each visible cell contains the account label, state, Open FourFold, Reload, and Close actions, and its own WebView2 profile. Two-cell layouts show only the first two slots; 2×2 shows all four.

- [ ] Step 4: Launch visible assigned slots independently.

For each visible assigned account, create or reuse its profile view and navigate to FourFoldDestination.StartUri. Catch failures per slot and show Initializing, Loading, Active, or Error beside that cell. One failed view must not cancel other launches. Do not keep a scrolling activity history.

~~~csharp
foreach (var slot in visibleSlots)
{
    try
    {
        await OpenSlotAsync(slot, cancellationToken);
    }
    catch (Exception exception)
    {
        slot.SetError(ToSafeUserMessage(exception));
    }
}
~~~

- [ ] Step 5: Run core tests and manually verify the panel.

Run: dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj --configuration Release
Manual checks: 1×2 is side-by-side; 2×1 is stacked; 2×2 fits four views; hidden slots preserve accounts; only visible slots navigate; failure in one cell leaves others working; window resizing does not clip the views.

- [ ] Step 6: Commit the account and panel UI.

Commit: git add src/FourFoldAccountManager.Desktop; git commit -m "feat: add account panel UI"

### Task 6: Finish Removal, Runtime Handling, Packaging, and Documentation

**Files:**
- Modify: src/FourFoldAccountManager.Desktop/Services/AccountBrowserSessionService.cs
- Modify: src/FourFoldAccountManager.Desktop/Services/PanelLaunchCoordinator.cs
- Modify: src/FourFoldAccountManager.Desktop/ViewModels/MainViewModel.cs
- Modify: src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs
- Create: src/FourFoldAccountManager.Desktop/Services/WebView2RuntimeStatus.cs
- Create: scripts/publish-windows.ps1
- Create: README.md
- Modify: .gitignore
- Modify: src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj

**Interfaces:**
- Consumes: completed UI and Core behavior from Tasks 1–5.
- Produces: confirmed profile removal; CloseAllViewsAsync(CancellationToken); runtime availability messaging; documented local storage; a self-contained win-x64 publish.

- [ ] Step 1: Implement account removal and shutdown.

After the user confirms Remove, clear only that profile's CoreWebView2BrowsingDataKinds.AllProfile data, remove the view, clear that account ID from all slots, remove its metadata, and save both files. If profile clearing fails, keep the account and assignment. For an inactive profile, create a temporary hidden view with the same profile name, clear it on the UI dispatcher, then close it. App shutdown disposes views without clearing sessions.

- [ ] Step 2: Handle popup, redirect, and WebView2 Runtime failures.

Apply FourFoldNavigationPolicy to NavigationStarting and NewWindowRequested. Approved HTTPS popups return to the same account view; unknown hosts and non-HTTPS schemes show an inline error. At startup, detect missing Evergreen WebView2 Runtime, show a clear install action, and disable panel launch until available.

- [ ] Step 3: Add a Windows x64 publish script and user README.

~~~powershell
$ErrorActionPreference = 'Stop'
dotnet publish src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
~~~

Document account setup, manual login per view, all three layouts, local session paths, profile removal, WebView2 Runtime requirement, and excluded features. State that passwords are not collected.

- [ ] Step 4: Run Core tests and publish the Windows build.

Run:
~~~powershell
dotnet test FourFoldAccountManager.sln --configuration Release
.\scripts\publish-windows.ps1
~~~
Expected: Core tests pass and the win-x64 application starts on Windows with the Evergreen WebView2 Runtime.

- [ ] Step 5: Complete the manual acceptance pass and push the feature branch.

Check independent logins after restart; account remove/cancel; 1×2, 2×1, and 2×2 geometry; hidden-slot preservation; failure isolation; window scaling at 100% and 150%; and published-app startup.

Run:
~~~powershell
git add README.md scripts .gitignore src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj
git commit -m "docs: document Windows release"
git push -u origin feature/fourfold-account-manager
~~~
Expected: private remote has the feature branch, and the executable is in artifacts/win-x64/.





