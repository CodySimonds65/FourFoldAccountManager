# FourFold Account Manager Auto-Update Design

**Date:** 2026-09-21  
**Branch:** `feature/auto-update`

## Goal

Add a Windows-only, opt-in startup update flow that lets a user download a newer FourFold Account Manager standalone executable, verify it, and restart into the new version. Add the GitHub Actions release automation needed to publish the assets that the updater consumes.

## Agreed User Experience

1. The application starts normally and does not wait for GitHub before showing the main window.
2. After the main window has loaded, the application performs one background check against the latest stable GitHub Release for `CodySimonds65/FourFoldAccountManager`.
3. If the release is newer than the running application, the user sees a prompt containing the new version and a concise release-note preview.
4. The prompt offers an explicit `Yes` action to download and restart, and a `No` action to continue using the current version.
5. Accepting the update downloads the standalone Windows executable, verifies its SHA-256 checksum, prepares a temporary updater helper, exits the current app, replaces the running executable, and relaunches it.
6. The application continues to work normally when the network is unavailable, the release metadata is invalid, the user declines, the checksum fails, the current executable is not replaceable, or the update process otherwise fails. A failed update never overwrites the current executable.

The updater is intentionally not silent, does not run as a background service, does not check periodically, and does not modify saved profiles or panel settings.

## Scope and Constraints

- Windows `win-x64` only; no macOS or Linux build, release, or update work.
- Target framework remains `net10.0-windows10.0.17763.0`.
- The app continues to require the Microsoft Edge WebView2 Runtime.
- GitHub Releases is the update source; no new server or third-party updater package is introduced.
- Release tags and app versions use strict `MAJOR.MINOR.PATCH` values represented as `vMAJOR.MINOR.PATCH` tags.
- Update checks use HTTPS, a short request timeout, and a descriptive `User-Agent`.
- Release metadata and checksums are treated as untrusted input and are validated before use.
- Tests remain local-only and are excluded from the pull request, as previously requested.

## Release Contract

Each stable release publishes these assets using predictable names:

- `FourFoldAccountManager-v<version>-win-x64.zip` — self-contained multi-file portable bundle with the app executable and required published files.
- `FourFoldAccountManager-v<version>-win-x64-standalone.exe` — self-contained single-file executable used by the in-app updater.
- `FourFoldAccountManager-v<version>-checksums.txt` — standard SHA-256 lines for both binary assets.

The updater selects only the standalone asset whose name matches the release version and the exact expected `win-x64-standalone.exe` suffix. It selects the checksum line for that exact filename, requires a 64-character hexadecimal digest, downloads the asset over HTTPS, and compares the computed SHA-256 before any installation step.

The existing v1.1.0 release does not need to be rewritten for this feature. Future releases produced by the new workflow will contain the checksum asset required by the updater.

## Application Architecture

### Release metadata client

Create a focused GitHub release client under `src/FourFoldAccountManager.Desktop/Updates/`. It calls:

`https://api.github.com/repos/CodySimonds65/FourFoldAccountManager/releases/latest`

The client maps only the fields needed by the updater: tag name, release name/body, and asset names/download URLs. It rejects non-semver stable tags, draft/prerelease metadata, missing asset URLs, non-HTTPS URLs, and duplicate or ambiguous required assets. It does not execute or interpret release-note content.

### Update coordinator

Create an updater coordinator that owns the startup check and user decision flow. It receives the current app version and an injectable release client/downloader so the update behavior can be tested without making real network calls. It compares normalized `System.Version` values and only prompts when the remote version is strictly greater than the running version.

The coordinator is started from `MainWindow_Loaded` without delaying the existing account/settings initialization. It runs at most once per application process. Exceptions are caught at the boundary and reported as a quiet status/log result rather than shown as an error dialog.

### Download and verification

The downloader writes to a unique file in the user temp directory, enforces a reasonable maximum download size, and reports progress only through internal state; no progress UI is required for this minor update flow. It computes SHA-256 after download and deletes the temporary file when verification fails or the update is declined.

### Restart and replacement

The running app cannot replace its own executable while it is executing. To avoid that lock, the installer copies the current published executable to a unique temporary helper path, starts that helper in a private `--apply-update` mode, then shuts down the main app.

The helper:

1. Validates its argument count and that the source, target, and parent-process identifiers are valid.
2. Waits for the parent process to exit.
3. Copies the verified downloaded executable next to the target as a temporary `.new` file.
4. Replaces the target only after the `.new` file is complete, retaining a temporary backup during the replacement.
5. Starts the updated target executable without updater arguments.
6. Cleans up the downloaded asset, helper copy, `.new` file, and backup when possible.

If any replacement step fails, the helper leaves the original executable intact where possible, attempts to relaunch it, and exits. The main app displays a concise fallback message and release link if the helper cannot be started or the update cannot be prepared.

Updater mode is entered from `App.OnStartup` before `MainWindow` is created, so a helper process never opens the normal application window.

### UI integration

The update prompt uses the existing WPF message-dialog style rather than adding a persistent settings surface. The prompt includes the new version and a bounded, plain-text release-note preview. The update check is not exposed as a new recurring preference in this change.

## GitHub Actions Architecture

Add two Windows-only workflows under `.github/workflows/` based on the RAM release flow:

### `tag-on-merge.yml`

- Trigger on a merged pull request targeting `main`.
- Run on `windows-latest`.
- Check out the merge commit with full history.
- Determine the next strict semantic version from the project version and existing `v*` tags.
- Preserve idempotency when a retry sees an existing tag/release associated with the merge.
- Create and push the release tag, then dispatch `release.yml` against that tag.

The source project carries the current `<Version>` value. If a merged change did not advance it beyond the latest release tag, the workflow creates a release-only child commit that bumps the patch version before tagging, following RAM’s established behavior. No macOS-specific branching or packaging is included.

### `release.yml`

- Trigger by `workflow_dispatch` with a required semantic `version` input.
- Check out the tagged release commit without persisted credentials.
- Validate that the tag, project version, and requested version agree.
- Publish the portable Windows bundle and self-contained single-file executable.
- Generate the checksums asset from the exact published binary files.
- Create or repair the GitHub Release idempotently, attaching all three assets and generated release notes.
- Verify that the published release is non-draft, non-prerelease, points to the intended commit, and contains non-empty assets with matching checksums.

No signing secret or macOS packaging is required for this repository’s first updater release. The workflow must fail closed if the version is malformed, the tag points at another commit, or expected assets cannot be produced.

## Versioning

Add an explicit `<Version>1.1.0</Version>` to the desktop project so local builds and published builds expose a comparable application version. The release workflow may pass the validated release version explicitly during `dotnet publish` for a release-only version bump, ensuring the executable’s assembly version matches its release tag.

The updater compares the informational/assembly version embedded in the published app, not the executable filename.

## Error Handling and Safety

- GitHub API timeout, DNS failure, rate limiting, malformed JSON, or invalid version: silently skip the check.
- Missing standalone asset or checksum: silently skip the prompt and record a diagnostic result.
- Download interruption or oversized asset: delete the partial file and keep the current app running.
- Checksum mismatch: never launch or copy the downloaded file; delete it.
- User declines: do nothing else for this process.
- Running from an unsupported host process such as `dotnet run`: do not attempt in-place installation; offer the release page only if the user explicitly accepts the update.
- Target directory not writable: keep the existing executable and show a concise failure message with the release page as a manual fallback.
- Release notes are displayed as text only; no links or commands from release notes are executed.

## Testing Strategy

Tests will be created and run locally in the ignored `tests/` directory, but test files and project changes will not be committed to the PR.

The local suite will cover:

- strict semantic version comparison and “no prompt for equal/older” behavior;
- valid release metadata and rejection of prereleases, malformed tags, duplicate assets, non-HTTPS URLs, and missing checksums;
- checksum verification success and mismatch cleanup;
- update decline and network/error paths remaining non-blocking;
- updater argument parsing and replacement sequencing using temporary files/process fixtures where practical.

Verification will also include a Release build, a self-contained publish, inspection of the generated asset names/checksum content, and a local smoke test of the published executable’s normal startup path. GitHub Actions behavior will be validated by YAML/static checks locally; the full release workflow is verified by the eventual Windows GitHub Actions run.

## Files and Boundaries

Expected product files:

- `src/FourFoldAccountManager.Desktop/Updates/UpdateModels.cs` — validated release/update data types.
- `src/FourFoldAccountManager.Desktop/Updates/GitHubReleaseClient.cs` — GitHub API metadata retrieval and validation.
- `src/FourFoldAccountManager.Desktop/Updates/UpdateDownloader.cs` — asset download and checksum verification.
- `src/FourFoldAccountManager.Desktop/Updates/UpdateInstaller.cs` — helper preparation, updater-mode replacement, relaunch, and cleanup.
- `src/FourFoldAccountManager.Desktop/Updates/UpdateCoordinator.cs` — startup orchestration and user-facing decision flow.
- `src/FourFoldAccountManager.Desktop/App.xaml` / `App.xaml.cs` — route updater mode before normal WPF startup.
- `src/FourFoldAccountManager.Desktop/MainWindow.xaml.cs` — start the non-blocking update check after load.
- `src/FourFoldAccountManager.Desktop/FourFoldAccountManager.Desktop.csproj` — explicit app version and any required publish properties.
- `.github/workflows/tag-on-merge.yml` — Windows tag/dispatch workflow.
- `.github/workflows/release.yml` — Windows publish and GitHub Release workflow.
- `README.md` — document release artifacts and the startup update behavior.

No changes are planned to account storage, panel layout logic, WebView session management, or saved settings.
