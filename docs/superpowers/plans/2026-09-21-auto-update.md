# FourFold Account Manager Auto-Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Windows-only, startup-prompted, checksum-verified self-update flow and the GitHub Actions release pipeline that publishes the update assets.

**Architecture:** A small `Updates` subsystem will separate GitHub release parsing, asset download/checksum verification, update prompting, and executable replacement. The WPF app will start one non-blocking check after normal startup; a copied temporary helper executable will perform the replacement after the main process exits. Two Windows-only workflows will tag merged releases and publish a portable ZIP, standalone EXE, and checksums file.

**Tech Stack:** .NET 10 WPF, `HttpClient`, `System.Text.Json`, `System.Security.Cryptography`, GitHub Releases API, GitHub Actions on `windows-latest`, PowerShell, and local-only MSTest tests under the ignored `tests/` directory.

**Spec:** `docs/superpowers/specs/2026-09-21-auto-update-design.md`

## Global Constraints

- Windows `win-x64` only; no macOS or Linux jobs or updater behavior.
- Keep `net10.0-windows10.0.17763.0` and the Microsoft WebView2 dependency unchanged.
- Check only `https://api.github.com/repos/CodySimonds65/FourFoldAccountManager/releases/latest`.
- Prompt only for a strictly newer stable `vMAJOR.MINOR.PATCH` release.
- Verify the standalone asset’s SHA-256 before any replacement.
- Never overwrite the current executable unless the verified replacement file is complete.
- Tests live in ignored `tests/` and are never staged for the PR.
- Do not modify account storage, panel layout, WebView session, or saved-settings behavior.

## Review Focus

- A malformed, prerelease, duplicate, or non-HTTPS GitHub asset must not produce an update prompt; covered by metadata parser tests in Task 1.
- A checksum mismatch must delete the downloaded file and must never invoke installation; covered by downloader tests in Task 2.
- A declined update or unavailable network must leave startup and existing settings behavior unchanged; covered by coordinator tests in Task 3.
- A running executable must not be overwritten while locked, and a failed replacement must preserve/relaunch the original; covered by installer tests in Task 4.
- A release retry or concurrent workflow run must not create a duplicate or wrong-target release; covered by PowerShell validation/static checks in Task 6 and the GitHub Actions run.

## File Map

Product files:

- Create `src/FourFoldAccountManager.Desktop/Updates/UpdateModels.cs` for release, asset, package, and install-result records.
- Create `src/FourFoldAccountManager.Desktop/Updates/GitHubReleaseClient.cs` for GitHub API retrieval and strict release/asset validation.
- Create `src/FourFoldAccountManager.Desktop/Updates/UpdateDownloader.cs` for bounded download and SHA-256 verification.
- Create `src/FourFoldAccountManager.Desktop/Updates/UpdateInstaller.cs` for helper preparation, updater-mode replacement, relaunch, and cleanup.
- Create `src/FourFoldAccountManager.Desktop/Updates/UpdateCoordinator.cs` for one-shot startup orchestration and prompt decisions.
- Modify `src/FourFoldAccountManager.Desktop/App.xaml` and `App.xaml.cs` to route helper mode before normal WPF startup.
- Modify `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs` to start the update check after the existing load path.
- Modify `src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj` to add the explicit `1.1.0` version.
- Create `.github/workflows/tag-on-merge.yml` for Windows tag creation and release dispatch.
- Create `.github/workflows/release.yml` for Windows publishing and idempotent release assets.
- Modify `README.md` with the update/release behavior and asset names.

Local-only files, never staged:

- Create `tests/FourFoldAccountManager.Tests/FourFoldAccountManager.Tests.csproj`.
- Create `tests/FourFoldAccountManager.Tests/ReleaseMetadataTests.cs`.
- Create `tests/FourFoldAccountManager.Tests/UpdateDownloaderTests.cs`.
- Create `tests/FourFoldAccountManager.Tests/UpdateCoordinatorTests.cs`.
- Create `tests/FourFoldAccountManager.Tests/UpdateInstallerTests.cs`.
- Create `tests/ValidateWorkflows.ps1`.

### Task 1: Establish the local test harness and release metadata model

**Files:**
- Create: `tests/FourFoldAccountManager.Tests/FourFoldAccountManager.Tests.csproj` (ignored local test project)
- Create: `tests/FourFoldAccountManager.Tests/ReleaseMetadataTests.cs` (ignored local tests)
- Create: `src/FourFoldAccountManager.Desktop/Updates/UpdateModels.cs`
- Create: `src/FourFoldAccountManager.Desktop/Updates/GitHubReleaseClient.cs`

**Interfaces:**
- Produces `UpdateAsset(string Name, Uri DownloadUrl, long Size)`.
- Produces `UpdateRelease(Version Version, string TagName, string Name, string Notes, IReadOnlyList<UpdateAsset> Assets)`.
- Produces `IUpdateReleaseClient.GetLatestAsync(CancellationToken)` returning `Task<UpdateRelease?>`.
- Produces `IUpdateInstaller.TryStart(string verifiedUpdatePath, string currentExecutablePath, int parentProcessId)` returning `UpdateInstallResult`, plus `UpdateInstallResult.Started`, `UpdateInstallResult.UnsupportedHost`, and `UpdateInstallResult.Failed`.
- Produces `UpdateReleaseParser.TryParse(string json, out UpdateRelease? release)` returning `false` for invalid or unsafe metadata.

- [ ] **Step 1: Create the ignored MSTest project and prove the empty suite runs**

Create a `net10.0-windows10.0.17763.0` test project with `<EnableWindowsTargeting>true</EnableWindowsTargeting>`, a project reference to the desktop project, and `Microsoft.NET.Test.Sdk`, `MSTest.TestAdapter`, and `MSTest.TestFramework` package references. Run:

```powershell
dotnet test tests/FourFoldAccountManager.Tests/FourFoldAccountManager.Tests.csproj -c Release --nologo
```

Expected: the project restores and reports zero tests passed with exit code 0. Confirm `tests/` is excluded by `.git/info/exclude` and do not stage it.

- [ ] **Step 2: Write the failing release metadata tests**

Add tests for the exact parser contract:

```csharp
[TestClass]
public sealed class ReleaseMetadataTests
{
    [TestMethod]
    public void ParsesStableReleaseAndSelectsVersionedStandalonePackage()
    {
        var json = """
        {"tag_name":"v1.2.0","name":"FourFold Account Manager v1.2.0","body":"Bug fixes","draft":false,"prerelease":false,"assets":[
          {"name":"FourFoldAccountManager-v1.2.0-win-x64-standalone.exe","browser_download_url":"https://github.com/CodySimonds65/FourFoldAccountManager/releases/download/v1.2.0/FourFoldAccountManager-v1.2.0-win-x64-standalone.exe","size":123},
          {"name":"FourFoldAccountManager-v1.2.0-checksums.txt","browser_download_url":"https://github.com/CodySimonds65/FourFoldAccountManager/releases/download/v1.2.0/FourFoldAccountManager-v1.2.0-checksums.txt","size":256}
        ]}
        """;

        Assert.IsTrue(UpdateReleaseParser.TryParse(json, out var release));
        Assert.AreEqual(new Version(1, 2, 0), release!.Version);
        Assert.AreEqual("Bug fixes", release.Notes);
    }

    [DataTestMethod]
    [DataRow("v1.2")]
    [DataRow("1.2.0")]
    [DataRow("v1.2.0-beta.1")]
    public void RejectsNonStrictReleaseTags(string tag)
    {
        var json = ReleaseJson(tag, standaloneUrl: "https://github.com/update.exe", checksumUrl: "https://github.com/checksums.txt");
        Assert.IsFalse(UpdateReleaseParser.TryParse(json, out _));
    }

    [TestMethod]
    public void RejectsDuplicateStandaloneAssetsAndNonHttpsUrls()
    {
        var json = ReleaseJson("v1.2.0", standaloneUrl: "http://github.com/update.exe", checksumUrl: "https://github.com/checksums.txt", duplicateStandalone: true);
        Assert.IsFalse(UpdateReleaseParser.TryParse(json, out _));
    }
}
```

The helper `ReleaseJson` must be test-only and emit the same GitHub fields used by the parser. Run the focused test class and verify it fails because the parser/model do not exist.

- [ ] **Step 3: Implement the minimum model and parser**

Implement `UpdateReleaseParser.TryParse` using `System.Text.Json` with these exact rules:

```csharp
tag_name must match ^v(?<version>\d+\.\d+\.\d+)$
draft == false
prerelease == false
every required asset has a non-empty HTTPS browser_download_url
there is exactly one asset named FourFoldAccountManager-v<version>-win-x64-standalone.exe
there is exactly one asset named FourFoldAccountManager-v<version>-checksums.txt
asset size is positive
```

Keep the checksum asset as metadata; `UpdateDownloader` will fetch and parse its contents after the user accepts. The metadata parser should retain both asset URLs and sizes without reading release-note commands or links. Implement `GitHubReleaseClient` with an injected `HttpClient`, `Accept: application/vnd.github+json`, and `User-Agent: FourFoldAccountManager/<currentVersion>`. Return `null` for HTTP/network/JSON failures at the client boundary.

- [ ] **Step 4: Run the focused tests and the local suite**

Run:

```powershell
dotnet test tests/FourFoldAccountManager.Tests/FourFoldAccountManager.Tests.csproj -c Release --nologo --filter FullyQualifiedName~ReleaseMetadataTests
dotnet test tests/FourFoldAccountManager.Tests/FourFoldAccountManager.Tests.csproj -c Release --nologo
```

Expected: all metadata tests pass and the full local suite has zero failures.

- [ ] **Step 5: Commit the product model/parser only**

Do not stage `tests/`. Commit:

```powershell
git add src/FourFoldAccountManager.Desktop/Updates/UpdateModels.cs src/FourFoldAccountManager.Desktop/Updates/GitHubReleaseClient.cs
git commit -m "feat: add release metadata validation"
```

### Task 2: Add bounded download and checksum verification

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Updates/UpdateDownloader.cs`
- Create: `tests/FourFoldAccountManager.Tests/UpdateDownloaderTests.cs` (ignored local tests)

**Interfaces:**
- Produces `IUpdateDownloader.DownloadAndVerifyAsync(UpdateRelease release, CancellationToken cancellationToken)` returning `Task<string?>`; the result is a verified temp-file path or `null`.
- Constructor accepts an `HttpClient`, optional `string? temporaryDirectory`, and `long maximumBytes` with a default of 256 MB.

- [ ] **Step 1: Write the failing downloader tests**

Use a real `HttpClient` with a local `HttpMessageHandler` test double that serves an in-memory checksum document and binary payload. Add tests with these names and assertions:

```csharp
[TestMethod]
public async Task DownloadsPayloadAndReturnsVerifiedTemporaryPath()
{
    var payload = Encoding.UTF8.GetBytes("verified update");
    var release = CreateReleaseWithPayload(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());
    var path = await downloader.DownloadAndVerifyAsync(release, CancellationToken.None);
    Assert.IsNotNull(path);
    CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(path!));
    File.Delete(path!);
}

[TestMethod]
public async Task DeletesPayloadAndReturnsNullWhenChecksumDoesNotMatch()
{
    var release = CreateReleaseWithPayload(new string('0', 64));
    var path = await downloader.DownloadAndVerifyAsync(release, CancellationToken.None);
    Assert.IsNull(path);
    Assert.AreEqual(0, Directory.GetFiles(tempDirectory).Length);
}

[TestMethod]
public async Task RejectsPayloadLargerThanConfiguredLimit()
{
    var release = CreateReleaseWithPayload(new string('0', 64));
    var path = await downloaderWithOneByteLimit.DownloadAndVerifyAsync(release, CancellationToken.None);
    Assert.IsNull(path);
}
```

Run the focused class and verify it fails because `UpdateDownloader` does not exist.

- [ ] **Step 2: Implement streaming download and SHA-256 verification**

Select the exact standalone and checksum assets from `UpdateRelease`, require both URLs to be HTTPS, download the checksum asset with a 32 KB limit, parse a standard line of the form `<64-hex-digest> *<exact-standalone-file-name>`, and reject missing/duplicate/mismatched checksum lines. Then stream the standalone response to a GUID-named `.download` file under the temp directory, reject non-success responses, reject content lengths over 256 MB, stop streaming once the byte limit is exceeded, compute SHA-256 with `SHA256.HashDataAsync`, compare case-insensitively to the parsed digest, and delete the file on every failure or cancellation. Return the path only after the digest matches.

- [ ] **Step 3: Run the focused and full local suite**

```powershell
dotnet test tests/FourFoldAccountManager.Tests/FourFoldAccountManager.Tests.csproj -c Release --nologo --filter FullyQualifiedName~UpdateDownloaderTests
dotnet test tests/FourFoldAccountManager.Tests/FourFoldAccountManager.Tests.csproj -c Release --nologo
```

Expected: all tests pass with no warnings treated as errors.

- [ ] **Step 4: Commit the downloader**

```powershell
git add src/FourFoldAccountManager.Desktop/Updates/UpdateDownloader.cs
git commit -m "feat: verify downloaded update packages"
```

### Task 3: Add startup orchestration and update prompt behavior

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Updates/UpdateCoordinator.cs`
- Create: `tests/FourFoldAccountManager.Tests/UpdateCoordinatorTests.cs` (ignored local tests)

**Interfaces:**
- Produces `UpdateCoordinator(Version currentVersion, string currentExecutablePath, int currentProcessId, IUpdateReleaseClient releaseClient, IUpdateDownloader downloader, IUpdateInstaller installer, Func<UpdateRelease, Task<bool>> prompt, Action<string>? failureNotice = null)`.
- Produces `Task CheckForUpdateAsync(CancellationToken cancellationToken = default)`.
- Consumes `IUpdateReleaseClient`, `IUpdateDownloader`, `IUpdateInstaller`, and `UpdateRelease` from Tasks 1–2.

- [ ] **Step 1: Write failing coordinator tests**

Create in-memory fakes and test these exact cases:

```csharp
[TestMethod]
public async Task DoesNotPromptWhenRemoteVersionIsNotNewer()
{
    var coordinator = CreateCoordinator(currentVersion: new Version(1, 2, 0), remoteVersion: new Version(1, 2, 0));
    await coordinator.CheckForUpdateAsync();
    Assert.AreEqual(0, promptCount);
}

[TestMethod]
public async Task DeclinedUpdateDoesNotDownloadOrInstall()
{
    var coordinator = CreateCoordinator(remoteVersion: new Version(1, 3, 0), promptResult: false);
    await coordinator.CheckForUpdateAsync();
    Assert.AreEqual(1, promptCount);
    Assert.AreEqual(0, downloaderCalls);
    Assert.AreEqual(0, installerCalls);
}

[TestMethod]
public async Task AcceptedUpdateInstallsOnlyAfterVerifiedDownload()
{
    var coordinator = CreateCoordinator(remoteVersion: new Version(1, 3, 0), promptResult: true, downloadedPath: "verified.exe");
    await coordinator.CheckForUpdateAsync();
    Assert.AreEqual(1, downloaderCalls);
    Assert.AreEqual(1, installerCalls);
}

[TestMethod]
public async Task ReleaseClientFailureIsNonThrowingAndDoesNotPrompt()
{
    var coordinator = CreateCoordinator(releaseClientFailure: true);
    await coordinator.CheckForUpdateAsync();
    Assert.AreEqual(0, promptCount);
}
```

Run the focused class and verify it fails because the coordinator and installer interface do not exist.

- [ ] **Step 2: Implement coordinator decisions**

Use an interlocked one-shot guard so repeated calls do not perform a second check. Return quietly for `null`, equal, or older releases. For a newer release, call the prompt delegate; if it returns `false`, stop. If it returns `true`, call the downloader; if it returns `null`, stop; otherwise call the installer. Catch cancellation, network, metadata, and installer exceptions at the coordinator boundary and invoke `failureNotice` only for a user-visible install failure, never for a failed background check.

- [ ] **Step 3: Run focused and full local tests**

```powershell
dotnet test tests/FourFoldAccountManager.Tests/FourFoldAccountManager.Tests.csproj -c Release --nologo --filter FullyQualifiedName~UpdateCoordinatorTests
dotnet test tests/FourFoldAccountManager.Tests/FourFoldAccountManager.Tests.csproj -c Release --nologo
```

- [ ] **Step 4: Commit the coordinator**

```powershell
git add src/FourFoldAccountManager.Desktop/Updates/UpdateCoordinator.cs
git commit -m "feat: prompt for available updates"
```

### Task 4: Implement safe executable replacement and WPF startup integration

**Files:**
- Create: `src/FourFoldAccountManager.Desktop/Updates/UpdateInstaller.cs`
- Create: `tests/FourFoldAccountManager.Tests/UpdateInstallerTests.cs` (ignored local tests)
- Modify: `src/FourFoldAccountManager.Desktop/App.xaml`
- Modify: `src/FourFoldAccountManager.Desktop/App.xaml.cs`
- Modify: `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs`

**Interfaces:**
- Produces `UpdateInstaller.ReplaceTargetFile(string verifiedUpdatePath, string targetExecutablePath)` returning `bool` for deterministic file replacement.
- Produces `UpdateInstaller.TryRunReplacementMode(IReadOnlyList<string> args)` returning `bool` to indicate whether startup was consumed by updater mode.
- Consumes `UpdateCoordinator` from Task 3.

- [ ] **Step 1: Write failing installer tests**

Use temporary directories and test-only helper invocations to cover:

```csharp
[TestMethod]
public async Task ReplacementCopiesVerifiedPayloadOverTargetAfterParentExit()
{
    var sourcePath = Path.Combine(tempDirectory, "verified.exe");
    var targetPath = Path.Combine(tempDirectory, "current.exe");
    await File.WriteAllTextAsync(sourcePath, "new bytes");
    await File.WriteAllTextAsync(targetPath, "old bytes");

    Assert.IsTrue(UpdateInstaller.ReplaceTargetFile(sourcePath, targetPath));
    Assert.AreEqual("new bytes", await File.ReadAllTextAsync(targetPath));
    Assert.IsFalse(File.Exists(targetPath + ".new"));
    Assert.IsFalse(File.Exists(targetPath + ".backup"));
}

[TestMethod]
public void InvalidUpdaterArgumentsAreRejectedWithoutTouchingTarget()
{
    var original = File.ReadAllBytes(targetPath);
    Assert.IsFalse(UpdateInstaller.TryRunReplacementMode(new[] { "--apply-update", "bad" }));
    CollectionAssert.AreEqual(original, File.ReadAllBytes(targetPath));
}

[TestMethod]
public void UnsupportedDotnetHostDoesNotStartReplacement()
{
    Assert.AreEqual(UpdateInstallResult.UnsupportedHost, installer.TryStart(verifiedPath, "dotnet.exe", Environment.ProcessId));
}
```

Run the focused class and verify it fails because the installer does not exist.

- [ ] **Step 2: Implement installer argument parsing and replacement**

Implement `--apply-update <parentPid> <sourcePath> <targetPath>` parsing before normal WPF startup. For normal published execution, copy `Environment.ProcessPath` to a GUID-named helper executable in the temp directory, start it with `ProcessStartInfo.ArgumentList`, and return `Started`. Reject non-`.exe`/nonexistent source files, `dotnet.exe` hosts, nonpositive parent IDs, non-absolute paths, and source/target paths that resolve to the same file.

In helper mode, wait for the parent PID to exit, copy the verified source to `<target>.new`, use `File.Replace` with a unique backup when the target exists, fall back to `File.Move(..., overwrite: true)` only after the new file is complete, start the target without updater arguments, and clean temporary files. On failure, restore the backup when present and start the original target if it exists.

- [ ] **Step 3: Integrate App startup without `StartupUri`**

Remove `StartupUri="MainWindow.xaml"` from `App.xaml`. In `App.OnStartup`, call `UpdateInstaller.TryRunReplacementMode(e.Args)` before `base.OnStartup`; if it consumes the command line, call `Shutdown()` and return. Otherwise create and show `MainWindow` exactly once so normal startup remains unchanged.

- [ ] **Step 4: Start the non-blocking coordinator after normal load**

At the end of `MainWindow_Loaded`, construct the production dependencies with the current assembly version, a GitHub `HttpClient` whose timeout is 5 seconds, `UpdateDownloader`, and `UpdateInstaller`. Start `_ = CheckForUpdatesAsync()` after existing initialization has completed. Prompt with:

```csharp
var message = $"FourFold Account Manager {release.Version} is available.\n\n{TruncatePlainText(release.Notes, 1200)}\n\nDownload and restart now?";
return MessageBox.Show(this, message, "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;
```

Do not block account loading on the network. On install preparation failure, show one concise warning and open the stable release page only when the user explicitly chooses the manual fallback action.

- [ ] **Step 5: Run installer, integration, and full local tests**

```powershell
dotnet test tests/FourFoldAccountManager.Tests/FourFoldAccountManager.Tests.csproj -c Release --nologo --filter FullyQualifiedName~UpdateInstallerTests
dotnet build FourFoldAccountManager.sln -c Release --nologo
dotnet test tests/FourFoldAccountManager.Tests/FourFoldAccountManager.Tests.csproj -c Release --nologo
```

Expected: installer tests pass, the WPF solution builds with zero errors, and all local tests pass.

- [ ] **Step 6: Commit app updater integration**

```powershell
git add src/FourFoldAccountManager.Desktop/Updates/UpdateInstaller.cs src/FourFoldAccountManager.Desktop/App.xaml src/FourFoldAccountManager.Desktop/App.xaml.cs src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs
git commit -m "feat: add startup update installation"
```

### Task 5: Add explicit application version and release documentation

**Files:**
- Modify: `src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj`
- Modify: `README.md`

- [ ] **Step 1: Add the current release version**

Add `<Version>1.1.0</Version>` to the desktop project property group. Do not change the target framework or WebView2 package.

- [ ] **Step 2: Document the updater and release assets**

Add a short README section stating that stable Windows releases are published on GitHub, the app checks once at startup, prompts before downloading, verifies the standalone EXE, and restarts after installation. Document the exact asset names:

```text
FourFoldAccountManager-v<version>-win-x64.zip
FourFoldAccountManager-v<version>-win-x64-standalone.exe
FourFoldAccountManager-v<version>-checksums.txt
```

Clarify that WebView2 remains required and that no macOS build is provided.

- [ ] **Step 3: Build and commit metadata/docs**

```powershell
dotnet build FourFoldAccountManager.sln -c Release --nologo
git add src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj README.md
git commit -m "docs: document app version and updates"
```

### Task 6: Add Windows-only tag and release workflows

**Files:**
- Create: `.github/workflows/tag-on-merge.yml`
- Create: `.github/workflows/release.yml`

**Interfaces:**
- `tag-on-merge.yml` dispatches `release.yml` with `version` and a tag ref.
- `release.yml` publishes the three release assets named in Task 5.

- [ ] **Step 1: Add a static failing workflow check**

Create a local PowerShell validation script under ignored `tests/` that loads both YAML files as text and asserts the required strings are absent/present:

```powershell
Assert-Contains $tagWorkflow 'pull_request_target'
Assert-Contains $tagWorkflow 'types: [closed]'
Assert-Contains $releaseWorkflow 'workflow_dispatch'
Assert-Contains $releaseWorkflow 'win-x64'
Assert-Contains $releaseWorkflow 'PublishSingleFile=true'
Assert-Contains $releaseWorkflow 'FourFoldAccountManager-v'
Assert-NotContains $tagWorkflow 'macos-'
Assert-NotContains $releaseWorkflow 'macos-'
```

Run it before creating the workflows and verify it fails because the workflow files do not exist.

- [ ] **Step 2: Implement `tag-on-merge.yml` from the RAM flow**

Use `pull_request_target` for merged PRs into `main`, `contents: write`, `actions: write`, a non-canceling concurrency group, and `windows-latest`. Check out the exact merge SHA with `fetch-depth: 0`, read `<Version>` from the desktop project, inspect strict `v*` tags, and create a release-only child commit that increments the patch version when the source version is not newer than the latest tag. Reuse an existing tag associated with the merge on retries, then run:

```powershell
gh workflow run release.yml --repo $env:GITHUB_REPOSITORY --ref $env:RELEASE_TAG --field version=$env:RELEASE_VERSION
```

Do not include signing, macOS, or Linux branches.

- [ ] **Step 3: Implement `release.yml` publishing**

Use `workflow_dispatch` with required `version`, `contents: write`, `windows-latest`, and pinned checkout/setup-dotnet actions. Validate `v<version>` against the project `<Version>` and the checked-out commit. Publish:

```powershell
dotnet publish .\src\FourFoldAccountManager.Desktop\FourFoldAccountManager.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$env:RELEASE_VERSION -o $portableStage
dotnet publish .\src\FourFoldAccountManager.Desktop\FourFoldAccountManager.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:Version=$env:RELEASE_VERSION -o $standaloneStage
```

Rename the single-file output to `FourFoldAccountManager-v<version>-win-x64-standalone.exe`, zip the portable stage as `FourFoldAccountManager-v<version>-win-x64.zip`, and write `FourFoldAccountManager-v<version>-checksums.txt` with one lowercase SHA-256 line per binary. Upload those files as a workflow artifact, then create or repair a non-draft, non-prerelease release with `gh release create`/`gh release upload --clobber`. Refuse to repair a release whose tag or target commit differs from the workflow target.

- [ ] **Step 4: Run static workflow checks and inspect generated scripts**

Run `tests/ValidateWorkflows.ps1` and `git diff --check`. Expected: all required Windows/version/asset strings are present and no macOS strings exist. GitHub Actions will provide the authoritative YAML/workflow validation when the branch is pushed; do not claim local YAML parsing unless a YAML parser is installed and invoked explicitly.

- [ ] **Step 5: Commit workflows**

```powershell
git add .github/workflows/tag-on-merge.yml .github/workflows/release.yml
git commit -m "ci: publish Windows auto-update releases"
```

### Task 7: End-to-end local verification and branch handoff

**Files:**
- Modify only if verification exposes an issue in the product files above.

- [ ] **Step 1: Run the full local test suite**

```powershell
dotnet test tests/FourFoldAccountManager.Tests/FourFoldAccountManager.Tests.csproj -c Release --nologo
```

Expected: every local test passes with exit code 0. Keep the test directory unstaged.

- [ ] **Step 2: Build the solution from a clean output configuration**

```powershell
dotnet clean FourFoldAccountManager.sln -c Release --nologo
dotnet build FourFoldAccountManager.sln -c Release --nologo
```

Expected: zero errors and zero warnings.

- [ ] **Step 3: Produce and inspect both release shapes locally**

```powershell
$stage = Join-Path $env:TEMP ('fourfold-release-' + [guid]::NewGuid().ToString('N'))
$portable = Join-Path $stage 'portable'
$standalone = Join-Path $stage 'standalone'
dotnet publish .\src\FourFoldAccountManager.Desktop\FourFoldAccountManager.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $portable
dotnet publish .\src\FourFoldAccountManager.Desktop\FourFoldAccountManager.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $standalone
Test-Path (Join-Path $portable 'FourFoldAccountManager.Desktop.exe')
Test-Path (Join-Path $standalone 'FourFoldAccountManager.Desktop.exe')
```

Expected: both executable paths exist; run the standalone executable long enough to confirm it reaches the normal window without entering updater mode, then close it.

- [ ] **Step 4: Inspect version, status, and diff**

Run:

```powershell
git diff --check
git status --short
git log --oneline --decorate -8
```

Confirm only intended product/spec/plan/workflow files are tracked, `tests/` remains local-only, and no unrelated files changed.

- [ ] **Step 5: Request review before PR creation**

Use the branch review workflow after all fresh verification passes. Do not push or create the PR until the final review identifies no required changes.
