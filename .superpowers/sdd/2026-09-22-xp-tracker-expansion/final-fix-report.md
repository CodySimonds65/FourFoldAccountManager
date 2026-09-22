# Whole-branch review final fix pass — 2026-09-22

## Scope and result

Both Important findings are fixed in one pass. Work was performed directly in `C:/Users/Cody/Desktop/FourFoldAccountManager/.worktrees/xp-tracker-expansion`, starting from clean HEAD `c56164f`. No subagents were dispatched. The implementation plan, baseline design spec, and entire `review-bb77730..c56164f.diff` package were read before editing. This report did not previously exist, so this is its initial entry.

The code-review, TDD, and verification skills guided review validation, test-first changes, and fresh verification before committing. No solution/project membership, account identity, UI, credential, or production polling policy changes were made.

## Finding 1: expired rate after a successful sample with no valid interval

`XpTrackingSession.ApplySnapshot` refreshed `RatePerHour` only after a failed poll or when at least one class contributed a valid interval. A successful all-invalid sample advanced the latest snapshot/time without pruning the rate history. The estimate could therefore divide current remaining XP by a fully expired rate.

Moved `_window.GetRate(sampledAt)` to the common successful-sample path, after any valid interval is added. Every accepted snapshot now refreshes/prunes the trailing hour, including baseline-only and invalid samples. Invalid samples still set uncertainty and update the measurement baseline. Valid classes still contribute when another class resets. Fetch failure behavior is unchanged: the last number remains stale until a successful sample arrives. Session gain survives history pruning.

Coverage added:

- Two expiration cases: XP decrease with a still-present active class, and disappearance of the only class. Both begin with 100 XP over two minutes (3000 XP/hour); the next successful sample is at minute 62, exactly when the old interval ends at the trailing-hour boundary. Assertions cover null rate/estimate, empty intervals, preserved session gain, uncertainty, freshness, and latest snapshot/time.
- An invalid sample at minute 61 preserves the still-overlapping rate; a subsequent valid sample uses the new baseline and expires the old interval, producing 1500 XP/hour and cumulative 150 XP.
- A healthy Mage class contributes 50 XP and 1500 XP/hour while Scout resets, with uncertainty retained.

### RED before production change

Command:

```text
dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj -c Release --filter FullyQualifiedName~Successful_sample_without_valid_interval
```

Exit 1. Both theory cases failed at the rate assertion:

```text
Successful_sample_without_valid_interval_expires_rate_and_estimate(missingClass: False) [FAIL]
Successful_sample_without_valid_interval_expires_rate_and_estimate(missingClass: True) [FAIL]
Assert.Null() Failure: Value of type 'Nullable<double>' has a value
Expected: null
Actual:   3000
Failed!  - Failed:     2, Passed:     0, Skipped:     0, Total:     2
```

### GREEN

The same focused command after the fix exited 0:

```text
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 16 ms
```

The broader focused command also exited 0:

```text
dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj -c Release --filter FullyQualifiedName~XpTrackerExpansionTests
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 30 ms
```

## Finding 2: coordinator reset tests started live polling

`Start` immediately entered `RunLoopAsync`, whose first poll requests the ranking before checking account usernames. The old `WaitForPoll` helper's 100 ten-millisecond sleeps therefore waited on a live network request. Its name/message incorrectly described this as a no-network poll.

Added one internal constructor overload accepting `Func<CancellationToken, Task>? runPollingLoop`, stored in a readonly field. The existing public constructor delegates with null, selecting the unchanged `RunLoopAsync` implementation. `Start` invokes the selected delegate through the same task/cancellation lifecycle as before. There is no public test option, new polling interface, timer change, client/HTTP change, or additional test-only session mutation API.

The reset tests inject `_ => Task.CompletedTask`, so they exercise real Start/reset/state/event/session behavior synchronously without entering polling, making requests, or reading/writing tracker persistence. The existing HTTP client object is still constructed/disposed, but these tests never invoke it. Removed `WaitForPoll`, all retry sleeps, and dependence on the unrelated linking status. Strengthened assertions to include clearing the selected estimate and preserving the entire unrelated account state.

A separate regression verifies the injected loop is called once across two active accounts, is not cancelled when only one stops, is cancelled when the last stops, restarts for a newly active account, and receives cancellation on disposal. All fake loop behavior lives in tests; the internal overload is exposed through the already-existing friend test assembly.

### RED before seam implementation

After changing the tests to use the requested dependency, this command exited 1:

```text
dotnet test tests/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj -c Release --filter FullyQualifiedName~XpTrackerCoordinatorTests
XpTrackerCoordinatorTests.cs(16,31): error CS1729: 'XpTrackerCoordinator' does not contain a constructor that takes 2 arguments
XpTrackerCoordinatorTests.cs(58,31): error CS1729: 'XpTrackerCoordinator' does not contain a constructor that takes 2 arguments
XpTrackerCoordinatorTests.cs(102,31): error CS1729: 'XpTrackerCoordinator' does not contain a constructor that takes 2 arguments
```

This was followed by a behavioral RED, not treated as sufficient on its own. With only the constructor/field plumbing added, but before changing `Start` to use the delegate, ran:

```text
dotnet test tests/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj -c Release --filter FullyQualifiedName~Injected_polling_loop
Injected_polling_loop_starts_once_and_is_cancelled_on_stop_and_dispose [FAIL]
Assert.Single() Failure: The collection was empty
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 49 ms
```

Exit 1, at line 113: `Start` bypassed the injected loop. This interim RED still entered the old live loop and cancelled it in finally; it did not wait for network completion or assert anything about a server response. The final tests use only the injected no-network path.

### GREEN

After routing `Start` through `_runPollingLoop`, the focused coordinator command exited 0:

```text
dotnet test tests/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj -c Release --filter FullyQualifiedName~XpTrackerCoordinatorTests
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 35 ms
```

## Full verification

All commands below ran from the specified worktree after both fixes. Output excerpts retain the result/counts; routine restore and assembly-path output is omitted. All restores reported projects up to date.

```text
dotnet test tests/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj -c Release
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 30 ms
Exit: 0

dotnet test tests/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj -c Release
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 391 ms
Exit: 0

dotnet build FourFoldAccountManager.sln -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:00.64
Exit: 0

git diff --check
Exit: 0; no whitespace errors.
```

Git emitted existing working-copy normalization notices (`LF will be replaced by CRLF the next time Git touches it`) for edited files. These were warnings, not whitespace-check failures. No test failure remained after implementation. The full Desktop run includes the existing rendered WPF row/context-menu test.

## Files changed

1. `src/FourFoldAccountManager.Core/Tracking/XpTrackingSession.cs` — common rate-window refresh on every successful sample.
2. `tests/FourFoldAccountManager.Core.Tests/XpTrackerExpansionTests.cs` — expiration, retained history/rebaseline, and partial valid-class coverage (four additional test cases).
3. `src/FourFoldAccountManager.Desktop/Services/XpTrackerCoordinator.cs` — internal polling-loop injection; unchanged default production loop.
4. `tests/FourFoldAccountManager.Desktop.Tests/XpTrackerCoordinatorTests.cs` — no-network reset fixtures, no sleeps, stronger isolation/estimate assertions, and injected loop lifecycle regression.
5. `.superpowers/sdd/2026-09-22-xp-tracker-expansion/final-fix-report.md` — this report and verification evidence.

## Concerns and handoff

No outstanding concern for either requested finding. Live HTTP behavior and manual gameplay/fullscreen interactions were not exercised as acceptance checks in this pass; their implementation is unchanged. The tests intentionally isolate polling to verify reset boundaries, while the public constructor continues using the original production loop.

Commit message: `fix: expire xp rates and isolate coordinator tests`. The implementation, regressions, and report are committed together; resolve the containing commit with `git log -1 -- .superpowers/sdd/2026-09-22-xp-tracker-expansion/final-fix-report.md`.
