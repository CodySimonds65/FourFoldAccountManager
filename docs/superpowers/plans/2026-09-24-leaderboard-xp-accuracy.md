# Leaderboard XP Accuracy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop valid leaderboard gains from being discarded by normal multi-profile queue timing or a permanently stopped desktop heartbeat renewal loop.

**Architecture:** Keep server-side public-profile sampling and all-class scoring. Make the collector's stale-gap limit account for the time needed to sample the active queue, while retaining baseline resets for genuine interruption. Make desktop heartbeat renewal retry after a transient local persistence failure. Diagnose a reported mismatch using comparable time windows and fresh data.

**Tech Stack:** .NET 10, C#, ASP.NET Core background service, WPF desktop coordinator, xUnit, existing Testcontainers PostgreSQL tests.

**Spec:** `docs/superpowers/specs/2026-09-24-xp-calculator-and-leaderboard-accuracy-design.md`; preserve the all-class and UTC observation rules in `docs/superpowers/specs/2026-09-23-shared-xp-leaderboard-design.md`.

## Global Constraints

- Leaderboard XP gained sums valid progress across every class by player ID. Local session XP measures only the active class and may be reset independently.
- Daily, ISO-weekly, and monthly leaderboard periods use UTC; score a valid interval at the server observation time, without invented boundary splits.
- Preserve the approved minimum interval between public-profile requests. Do not score a failed fetch, stale active lease, deactivation gap, username mismatch, or invalid class delta.
- Do not change the database schema or submit XP from the desktop client.
- Existing scored events remain after deactivation; a renewed profile starts from a fresh baseline when participation was genuinely interrupted.

## Review Focus

- Three or more continuously active profiles with successful fetches score on their next queued sample; a same-window local session for one of them must not exceed its server-scored active-class gain solely because of queue delay. Test in Task 1 and Task 2.
- A single profile still rebaselines after a genuinely stale gap; test in Task 2.
- A failed fetch or expired/deactivated lease still prevents a later catch-up gain; test in Task 2 and existing store tests.
- A transient local heartbeat persistence failure does not end future renewals; test in Task 3.
- A cached or previous UTC-period page is not treated as the current measured total during comparison; verify in Task 4.

## File Map

| File | Responsibility |
| --- | --- |
| `src/FourFoldAccountManager.Leaderboard.Tests/Collection/LeaderboardSamplingServiceTests.cs` | Reproduce queue-related missing gains and protect genuine rebaseline cases. |
| `src/FourFoldAccountManager.Leaderboard.Service/Collection/LeaderboardSamplingService.cs` | Apply a queue-aware maximum valid sampling gap without changing request spacing. |
| `src/FourFoldAccountManager.Desktop.Tests/Leaderboard/LeaderboardCoordinatorTests.cs` | Verify renewal retries after one transient persistence failure. |
| `src/FourFoldAccountManager.Desktop/Services/LeaderboardCoordinator.cs` | Keep the renewal loop alive after recoverable local I/O errors. |
| `src/FourFoldAccountManager.Leaderboard.Tests/Data/LeaderboardStoreTests.cs` | Existing integration checks for deactivation, lease expiry, and saved gains. |
| `README.md` | Explain why local session XP and a leaderboard period total can differ. |

---

### Task 1: Reproduce missing gains caused by normal queue time

**Files:**
- Modify: `src/FourFoldAccountManager.Leaderboard.Tests/Collection/LeaderboardSamplingServiceTests.cs`

**Interfaces:**
- Consumes: `LeaderboardSamplingService.RunOnceAsync`, the existing `FakeStore`, `Snapshot`, and `LeaderboardCollectionOptions` helpers.
- Produces: a failing test that isolates the three-profile gap from heartbeat and parser behavior.

- [ ] **Step 1: Add a source that returns two snapshots for each player ID.** Keep it in the existing test class, and return a fresh baseline snapshot at 10 absolute XP followed by 35 absolute XP.

```csharp
private sealed class PerPlayerSource : ILeaderboardPublicProfileSource
{
    private readonly Dictionary<int, Queue<PlayerProgressSnapshot>> _responses;
    public PerPlayerSource(params int[] playerIds) => _responses = playerIds.ToDictionary(
        id => id, _ => new Queue<PlayerProgressSnapshot>(
            [Snapshot("Alice", 10) with { ActiveClassName = "Warrior" },
             Snapshot("Alice", 35) with { ActiveClassName = "Warrior" }]));
    public Task<PlayerProgressSnapshot> FetchAsync(int playerId, CancellationToken ct) =>
        Task.FromResult(_responses[playerId].Dequeue());
}
```

- [ ] **Step 2: Add `ThreeContinuouslyActivePlayersDoNotLoseQueuedGain`.** Activate IDs 1, 2, and 3 with username Alice, use a 20 ms minimum interval, run the first pass at `Now` and the second at `Now.AddMilliseconds(61)`, and assert three 25-XP gains. Also calculate a local active-class session from the same two snapshots and assert its 25-XP gain. This directly reproduces the reported direction of discrepancy: local session gain exceeds a leaderboard score discarded only because the server queue took longer than three intervals.

```csharp
var store = new FakeStore();
foreach (var id in new[] { 1, 2, 3 }) store.Activate(id, "Alice", Now);
var before = Snapshot("Alice", 10) with { ActiveClassName = "Warrior" };
var after = Snapshot("Alice", 35) with { ActiveClassName = "Warrior" };
var local = new XpTrackingSession();
local.ApplySnapshot(before, Now);
local.ApplySnapshot(after, Now.AddMilliseconds(61));
Assert.Equal(25, local.SessionGain);
var service = new LeaderboardSamplingService(store, new PerPlayerSource(1, 2, 3),
    new LeaderboardCollectionOptions
    {
        Enabled = true,
        MinimumSampleInterval = TimeSpan.FromMilliseconds(20)
    });
await service.RunOnceAsync(Now, default);
await service.RunOnceAsync(Now.AddMilliseconds(61), default);
Assert.Equal(new long[] { 25, 25, 25 }, store.Gains.Order().ToArray());
```

- [ ] **Step 3: Run this test and capture its failing output.** The current fixed three-interval cutoff is expected to skip the gains.

```powershell
dotnet test src/FourFoldAccountManager.Leaderboard.Tests/FourFoldAccountManager.Leaderboard.Tests.csproj --filter FullyQualifiedName~ThreeContinuouslyActivePlayersDoNotLoseQueuedGain
```

- [ ] **Step 4: Commit the reproduction.**

```powershell
git add src/FourFoldAccountManager.Leaderboard.Tests/Collection/LeaderboardSamplingServiceTests.cs
git commit -m "test: reproduce queued leaderboard gain loss"
```

### Task 2: Distinguish normal queue delay from a stale observation

**Files:**
- Modify: `src/FourFoldAccountManager.Leaderboard.Service/Collection/LeaderboardSamplingService.cs`
- Modify: `src/FourFoldAccountManager.Leaderboard.Tests/Collection/LeaderboardSamplingServiceTests.cs`

**Interfaces:**
- Consumes: the deduplicated active-profile count at the start of `RunOnceAsync` and `options.MinimumSampleInterval`.
- Produces: a queue-aware maximum gap passed to `SampleAsync`, retaining the existing `NeedsBaseline` and active-lease checks.

- [ ] **Step 1: Add a regression for a gap beyond the queue-aware limit.** With three active profiles and a 20 ms minimum interval, seed player 1's baseline at `Now`, fetch again after more than four intervals, and assert no gain plus a fresh baseline. Retain `RebaselinesAfterUncertainGapWithoutWritingGain`, `SourceFailureRequestsRebaselineAndNextSuccessScoresNothing`, and the active-lease tests.

```csharp
var options = new LeaderboardCollectionOptions
{
    Enabled = true,
    MinimumSampleInterval = TimeSpan.FromMilliseconds(20)
};
var store = Seed(10);
store.Activate(2, "Alice", Now);
store.Activate(3, "Alice", Now);
var source = new FakeSource(Snapshot("Alice", 35), Snapshot("Alice", 10),
    Snapshot("Alice", 10));
await new LeaderboardSamplingService(store, source, options)
    .RunOnceAsync(Now.AddMilliseconds(100), default);
Assert.Empty(store.Gains);
Assert.False(store.States[1].NeedsBaseline);
```

- [ ] **Step 2: Compute the maximum accepted gap from the deduplicated queue length.** A normal pass revisits each player after approximately one interval per queued profile. Allow one extra interval for pass turnover, keep at least the existing three-interval floor, and pass this bound to `SampleAsync`. Do not reduce the delay between public-profile requests.

```csharp
var profiles = active.GroupBy(x => x.PlayerId).Select(x => x.First())
    .OrderBy(x => x.PlayerId).ToArray();
var maxSampleGap = options.MinimumSampleInterval * Math.Max(3, profiles.Length + 1);
foreach (var profile in profiles)
{
    ct.ThrowIfCancellationRequested();
    if (fetched) await Task.Delay(options.MinimumSampleInterval, _clock, ct);
    var observedAt = fetched ? now + _clock.GetElapsedTime(start) : now;
    var currentActive = await store.GetActiveProfilesAsync(observedAt - options.ActiveLeaseDuration, ct);
    var currentProfile = currentActive.FirstOrDefault(x => x.PlayerId == profile.PlayerId);
    if (currentProfile is null) continue;
    fetched = await SampleAsync(currentProfile.PlayerId, currentProfile.Username,
        observedAt, maxSampleGap, ct);
}
```

- [ ] **Step 3: Replace only the fixed-gap check.** Keep first-sample, `NeedsBaseline`, source failure, username mismatch, invalid-class, and active-lease behavior unchanged.

```csharp
if (now - previous.LastSampledAtUtc > maxSampleGap)
{
    await SaveAsync(null);
    return true;
}
```

- [ ] **Step 4: Run collector and full leaderboard tests.** The three-profile test must now pass; the genuine stale-gap and failure tests must still pass. PostgreSQL integration tests require Docker.

```powershell
dotnet test src/FourFoldAccountManager.Leaderboard.Tests/FourFoldAccountManager.Leaderboard.Tests.csproj --filter FullyQualifiedName~LeaderboardSamplingServiceTests
dotnet test src/FourFoldAccountManager.Leaderboard.Tests/FourFoldAccountManager.Leaderboard.Tests.csproj
```

- [ ] **Step 5: Commit the scoring fix.**

```powershell
git add src/FourFoldAccountManager.Leaderboard.Service/Collection/LeaderboardSamplingService.cs src/FourFoldAccountManager.Leaderboard.Tests/Collection/LeaderboardSamplingServiceTests.cs
git commit -m "fix: retain gains across normal leaderboard queues"
```

### Task 3: Keep desktop heartbeat renewal alive after transient local I/O failure

**Files:**
- Modify: `src/FourFoldAccountManager.Desktop/Services/LeaderboardCoordinator.cs`
- Modify: `src/FourFoldAccountManager.Desktop.Tests/Leaderboard/LeaderboardCoordinatorTests.cs`

**Interfaces:**
- Consumes: `QueueParticipationAsync`, `FlushPendingAsync`, and the one-minute renewal cadence.
- Produces: an internal renewal-loop helper that accepts a tick delegate and interval, allowing a deterministic test of one failed tick followed by a successful tick.

- [ ] **Step 1: Add `RenewalRetriesAfterOneLocalSaveFailure`.** Call an internal renewal-loop helper with a short interval and a tick delegate that throws `IOException` once, then succeeds and cancels the test token. Assert the delegate is called twice. The current loop would exit on the first exception.

```csharp
var calls = 0;
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
await LeaderboardCoordinator.RunRenewalLoopCoreAsync(token =>
{
    calls++;
    if (calls == 1) throw new IOException("temporary state-save failure");
    cts.Cancel();
    return Task.CompletedTask;
}, TimeSpan.FromMilliseconds(20), cts.Token);
Assert.Equal(2, calls);
```

- [ ] **Step 2: Run the new test and confirm it fails because the helper does not exist yet.**

```powershell
dotnet test src/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj --filter FullyQualifiedName~RenewalRetriesAfterOneLocalSaveFailure
```

- [ ] **Step 3: Move the timer loop into that internal helper and catch `IOException` and `UnauthorizedAccessException` around individual ticks.** Continue to the next tick after those recoverable local persistence errors; let cancellation exit normally. Keep the coordinator's existing choice between queuing an active heartbeat and flushing a pending one.

```csharp
internal static async Task RunRenewalLoopCoreAsync(
    Func<CancellationToken, Task> tick, TimeSpan interval, CancellationToken ct)
{
    using var timer = new PeriodicTimer(interval);
    try
    {
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { await tick(ct); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
}
```

- [ ] **Step 4: Run the focused desktop tests.** Also retain the existing tests that active IDs are sent only for open/resolved profiles and that unchanged tracker polls do not add heartbeats.

```powershell
dotnet test src/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj --filter FullyQualifiedName~LeaderboardCoordinatorTests
```

- [ ] **Step 5: Commit the renewal fix.**

```powershell
git add src/FourFoldAccountManager.Desktop/Services/LeaderboardCoordinator.cs src/FourFoldAccountManager.Desktop.Tests/Leaderboard/LeaderboardCoordinatorTests.cs
git commit -m "fix: retry leaderboard heartbeat renewal"
```

### Task 4: Reconcile and document the two XP displays

**Files:**
- Modify: `src/FourFoldAccountManager.Leaderboard.Tests/Collection/LeaderboardSamplingServiceTests.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: the existing all-class scoring, active-class session tracking, UTC period windows, and leaderboard freshness display.
- Produces: a clear explanation of expected differences and a completed verification record in the implementation summary.

- [ ] **Step 1: Review the existing deactivation tests before changing any related behavior.** `DeactivationAndReactivationRebaselineEvenWithinSampleInterval`, `LongSamplingIntervalStillRebaselinesAfterLeaseExpiresAndReactivates`, and `ObservationAfterOptOutPreservesBaselineAndWritesNoGain` describe intended protection against uncertain catch-up gains.

```powershell
dotnet test src/FourFoldAccountManager.Leaderboard.Tests/FourFoldAccountManager.Leaderboard.Tests.csproj --filter "FullyQualifiedName~DeactivationAndReactivationRebaselineEvenWithinSampleInterval|FullyQualifiedName~LongSamplingIntervalStillRebaselinesAfterLeaseExpiresAndReactivates|FullyQualifiedName~ObservationAfterOptOutPreservesBaselineAndWritesNoGain"
```

- [ ] **Step 2: Add one concise README paragraph explaining comparison conditions.** State that local session XP tracks the active class from its own start/reset, while leaderboard XP totals all valid classes in the selected UTC period, beginning after its first baseline. Mention that a cached page can lag and that deactivation or a missed sample can omit an uncertain interval.

```markdown
Session XP and leaderboard XP cover different scopes: the tracker counts the active class from its local session start or reset, while the leaderboard totals valid gains across all classes in the selected UTC period after its first baseline. A saved leaderboard page may lag; interrupted participation or uncertain samples are not backfilled.
```

- [ ] **Step 3: Add a comparison test using the same two profile snapshots.** Baseline Warrior and Mage at 10 absolute XP each, then advance Warrior to 35 and Mage to 20. Assert the leaderboard collector scores 35 total XP and a local `XpTrackingSession` with Warrior active scores 25 session XP. This protects the user-confirmed all-class leaderboard rule while demonstrating why the displayed values can differ.

```csharp
var before = Snapshot("Alice", 10, 10) with { ActiveClassName = "Warrior" };
var after = Snapshot("Alice", 35, 20) with { ActiveClassName = "Warrior" };
var local = new XpTrackingSession();
local.ApplySnapshot(before, Now);
local.ApplySnapshot(after, Now.AddMinutes(1));
Assert.Equal(25, local.SessionGain);
var store = Seed(10, 10);
await Create(store, new FakeSource(after)).RunOnceAsync(Now.AddMinutes(1), default);
Assert.Equal(35, Assert.Single(store.Gains));
```

- [ ] **Step 4: Build and run the three test projects.** Record whether Docker-backed PostgreSQL tests ran or were unavailable, and report any failure separately from the scoped fixes.

```powershell
dotnet build FourFoldAccountManager.sln -c Release
dotnet test src/FourFoldAccountManager.Core.Tests/FourFoldAccountManager.Core.Tests.csproj
dotnet test src/FourFoldAccountManager.Desktop.Tests/FourFoldAccountManager.Desktop.Tests.csproj
dotnet test src/FourFoldAccountManager.Leaderboard.Tests/FourFoldAccountManager.Leaderboard.Tests.csproj
```

- [ ] **Step 5: Reconcile a fresh daily leaderboard page with a controlled profile over one UTC window when a service and profile are available.** Record profile ID, baseline and later sample timestamps, per-class absolute XP deltas, gained-event total, and page `GeneratedAtUtc`. Sum all valid class deltas and compare them with the server total; compare local session XP only for the active class and overlapping time. Use the Task 4 comparison test as the reproducible verification when no live service is available.

- [ ] **Step 6: Commit the comparison test and documentation.**

```powershell
git add README.md src/FourFoldAccountManager.Leaderboard.Tests/Collection/LeaderboardSamplingServiceTests.cs
git commit -m "docs: explain leaderboard and session xp scopes"
```

### Review amendment: use completed-pass timing

The count-based cutoff in Task 2 fails when the active queue shrinks between passes or successful profile fetches add latency. Replace it with a singleton sampling schedule shared by the worker's scoped samplers. For a player sampled in the previous successful pass, accept the actual prior pass duration, the short idle period before the next pass, and the time spent reaching that player's new observation, with the existing three-interval floor. If the worker was idle for more than three intervals, the previous pass failed, or the player was absent from that pass, use the three-interval cutoff and rebaseline uncertain gaps. Keep fetch failures and participation rules unchanged. Regression tests cover queue shrinkage across new sampler scopes, successful fetch latency, and an idle worker.
